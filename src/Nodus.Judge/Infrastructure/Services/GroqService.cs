using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.Logging;
using Nodus.Judge.Application.Interfaces.Services;
using Nodus.Judge.Domain.Common;
using System.Diagnostics;

namespace Nodus.Judge.Infrastructure.Services;

public sealed class GroqService : IGroqService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GroqService> _logger;
    private const string DefaultModel = "openai/gpt-oss-20b";

    public GroqService(HttpClient httpClient, ILogger<GroqService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result<string>> GenerateAsync(string prompt, string? model = null, CancellationToken ct = default)
    {
        try
        {
            var apiKey = await GetApiKeyAsync();
            if (apiKey == null) return Result<string>.Fail("GROQ API key not configured (use Settings)");

            _logger?.LogDebug("GROQ API key found, proceeding with request.");
            Debug.WriteLine("[GroqService] GROQ API key found, proceeding with request.");

            var body = new
            {
                model = model ?? DefaultModel,
                input = prompt
            };

            using var res = await SendJsonAsync("responses", body, apiKey, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger?.LogError("Groq API error {Status}: {Body}", res.StatusCode, err);
                Debug.WriteLine($"[GroqService] Groq API error {res.StatusCode}: {err}");
                return Result<string>.Fail($"Groq API error: {res.StatusCode} {err}");
            }

            var respText = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            _logger?.LogDebug("Groq response: {Response}", respText);
            Debug.WriteLine($"[GroqService] Groq response: {respText}");
            try
            {
                using var doc = JsonDocument.Parse(respText);
                if (doc.RootElement.TryGetProperty("output_text", out var outText) && outText.ValueKind == JsonValueKind.String)
                    return Result<string>.Ok(outText.GetString() ?? string.Empty);

                // Fallback: return root as string if possible
                if (doc.RootElement.ValueKind == JsonValueKind.Object || doc.RootElement.ValueKind == JsonValueKind.Array)
                    return Result<string>.Ok(doc.RootElement.ToString());

                return Result<string>.Ok(respText);
            }
            catch (JsonException)
            {
                return Result<string>.Ok(respText);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Groq request exception");
            Debug.WriteLine($"[GroqService] Exception: {ex}");
            return Result<string>.Fail($"Groq request failed: {ex.Message}");
        }
    }

    public async Task<Result<string>> GenerateStructuredSummaryAsync(string projectJson, string? model = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectJson))
            return Result<string>.Fail("Project payload is empty");

        try
        {
            var apiKey = await GetApiKeyAsync();
            if (apiKey == null) return Result<string>.Fail("GROQ API key not configured (use Settings)");

            var chatBody = new
            {
                model = model ?? DefaultModel,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Eres un asistente para jueces de proyectos. Devuelve SOLO JSON válido, breve y claro en español." 
                    },
                    new
                    {
                        role = "user",
                        content = "Genera un resumen del proyecto con 2-3 frases y 3-5 bullets accionables a partir de este JSON:\n" + projectJson
                    }
                },
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "project_summary",
                        strict = true,
                        schema = new
                        {
                            type = "object",
                            additionalProperties = false,
                            properties = new
                            {
                                summary = new { type = "string", minLength = 20 },
                                bullets = new
                                {
                                    type = "array",
                                    minItems = 3,
                                    maxItems = 5,
                                    items = new { type = "string", minLength = 8 }
                                }
                            },
                            required = new[] { "summary", "bullets" }
                        }
                    }
                }
            };

            using var structuredRes = await SendJsonAsync("chat/completions", chatBody, apiKey, ct).ConfigureAwait(false);
            if (structuredRes.IsSuccessStatusCode)
            {
                var structuredText = await structuredRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (TryExtractChatCompletionJson(structuredText, out var contentJson))
                    return Result<string>.Ok(contentJson);
            }
            else
            {
                var err = await structuredRes.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger?.LogWarning("Structured summary path failed {Status}: {Body}", structuredRes.StatusCode, err);
                Debug.WriteLine($"[GroqService] Structured summary path failed {structuredRes.StatusCode}: {err}");
            }

            var fallbackPrompt = new StringBuilder();
            fallbackPrompt.AppendLine("Resume el siguiente proyecto en 2-3 frases y devuelve JSON con {summary, bullets[]}. Solo JSON válido.");
            fallbackPrompt.AppendLine(projectJson);
            return await GenerateAsync(fallbackPrompt.ToString(), model, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Groq structured summary exception");
            Debug.WriteLine($"[GroqService] Structured summary exception: {ex}");
            return Result<string>.Fail($"Groq structured summary failed: {ex.Message}");
        }
    }

    private async Task<string?> GetApiKeyAsync()
    {
        var apiKey = await SecureStorage.Default.GetAsync("GROQ_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey;

        _logger?.LogWarning("GROQ API key missing in SecureStorage");
        Debug.WriteLine("[GroqService] GROQ API key missing in SecureStorage");
        return null;
    }

    private async Task<HttpResponseMessage> SendJsonAsync(string relativePath, object body, string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, relativePath);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        _logger?.LogDebug("Sending Groq request to {Base}{Path}", _httpClient.BaseAddress, req.RequestUri);
        Debug.WriteLine($"[GroqService] Sending request to {_httpClient.BaseAddress}{req.RequestUri}");
        return await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
    }

    private static bool TryExtractChatCompletionJson(string response, out string contentJson)
    {
        contentJson = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return false;

            var first = choices[0];
            if (first.ValueKind != JsonValueKind.Object) return false;
            if (!first.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return false;
            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) return false;

            var text = content.GetString();
            if (string.IsNullOrWhiteSpace(text)) return false;
            contentJson = text;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
