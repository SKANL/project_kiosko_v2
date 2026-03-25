using System.Linq;
using System.Text;
using System.Text.Json;
using Nodus.Judge.Application.DTOs;

namespace Nodus.Judge.Infrastructure.Services
{
    public static class AiResultParser
    {
        private const string FallbackMessage = "No se pudo extraer un resumen legible. Intenta regenerarlo.";
        public const int DefaultDisplayMax = 1200;

        public static string ParseSummaryToDisplay(string? raw, int max = DefaultDisplayMax)
        {
            var full = ParseSummaryFull(raw);
            return TruncateForDisplay(full, max);
        }

        public static string ParseSummaryFull(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            raw = raw.Trim();

            // 0) If non-JSON text arrives directly, return it as-is
            if (!(raw.StartsWith("{") || raw.StartsWith("[")))
                return raw;

            // 1) Try JSON -> AiSummaryDto (expected format)
            try
            {
                var dto = JsonSerializer.Deserialize<AiSummaryDto>(raw);
                var dtoDisplay = FormatSummaryDto(dto);
                if (!string.IsNullOrWhiteSpace(dtoDisplay)) return dtoDisplay;
            }
            catch { }

            // 2) Parse JSON and extract only user-facing text from known response shapes
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (TryExtractPrimaryOutputText(root, out var textOutput))
                {
                    var normalized = NormalizePotentialSummaryJson(textOutput);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        return normalized;
                }

                if (root.ValueKind == JsonValueKind.Object)
                {
                    var summaryOnly = ExtractSummaryField(root);
                    if (!string.IsNullOrWhiteSpace(summaryOnly))
                        return summaryOnly;
                }

                if (root.ValueKind == JsonValueKind.Array)
                {
                    var arr = root.EnumerateArray().ToArray();
                    if (arr.All(a => a.ValueKind == JsonValueKind.String))
                        return string.Join("\n", arr.Select(a => a.GetString()));
                }
            }
            catch { }

            // 3) Never show raw JSON internals to end users in the standard summary UI.
            return FallbackMessage;
        }

        public static bool IsDisplayTruncated(string? raw, int max = DefaultDisplayMax)
        {
            var full = ParseSummaryFull(raw);
            return !string.IsNullOrWhiteSpace(full)
                && !string.Equals(full, FallbackMessage, StringComparison.Ordinal)
                && full.Length > max;
        }

        private static string FormatSummaryDto(AiSummaryDto? dto)
        {
            if (dto == null) return string.Empty;

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(dto.Summary))
                sb.AppendLine(dto.Summary.Trim());

            if (dto.Bullets != null && dto.Bullets.Count > 0)
            {
                sb.AppendLine();
                foreach (var b in dto.Bullets.Where(b => !string.IsNullOrWhiteSpace(b)))
                    sb.AppendLine($"- {b.Trim()}");
            }

            return sb.ToString().Trim();
        }

        private static string ExtractSummaryField(JsonElement root)
        {
            if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.String)
                return string.Empty;

            var dto = new AiSummaryDto
            {
                Summary = summary.GetString(),
                Bullets = root.TryGetProperty("bullets", out var bullets) && bullets.ValueKind == JsonValueKind.Array
                    ? bullets.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                    : null
            };

            return FormatSummaryDto(dto);
        }

        private static bool TryExtractPrimaryOutputText(JsonElement root, out string text)
        {
            text = string.Empty;

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            {
                text = outputText.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(text);
            }

            if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                var chunks = new List<string>();
                foreach (var item in output.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (!item.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String) continue;

                    var itemType = typeProp.GetString() ?? string.Empty;
                    if (!string.Equals(itemType, "message", StringComparison.OrdinalIgnoreCase))
                        continue; // ignore reasoning/tool items for end-user display

                    if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var block in content.EnumerateArray())
                        {
                            if (block.ValueKind != JsonValueKind.Object) continue;
                            if (!block.TryGetProperty("type", out var blockTypeProp) || blockTypeProp.ValueKind != JsonValueKind.String) continue;

                            var blockType = blockTypeProp.GetString() ?? string.Empty;
                            if (!(string.Equals(blockType, "output_text", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(blockType, "text", StringComparison.OrdinalIgnoreCase)))
                                continue;

                            if (block.TryGetProperty("text", out var blockText) && blockText.ValueKind == JsonValueKind.String)
                            {
                                var value = blockText.GetString();
                                if (!string.IsNullOrWhiteSpace(value)) chunks.Add(value.Trim());
                            }
                        }
                    }
                }

                if (chunks.Count > 0)
                {
                    text = string.Join("\n", chunks);
                    return true;
                }
            }

            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                var chunks = new List<string>();
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.ValueKind != JsonValueKind.Object) continue;

                    if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
                    {
                        if (message.TryGetProperty("content", out var content))
                        {
                            if (content.ValueKind == JsonValueKind.String)
                            {
                                var value = content.GetString();
                                if (!string.IsNullOrWhiteSpace(value)) chunks.Add(value.Trim());
                            }
                            else if (content.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var block in content.EnumerateArray())
                                {
                                    if (block.ValueKind != JsonValueKind.Object) continue;
                                    if (block.TryGetProperty("text", out var blockText) && blockText.ValueKind == JsonValueKind.String)
                                    {
                                        var value = blockText.GetString();
                                        if (!string.IsNullOrWhiteSpace(value)) chunks.Add(value.Trim());
                                    }
                                }
                            }
                        }
                    }
                    else if (choice.TryGetProperty("text", out var choiceText) && choiceText.ValueKind == JsonValueKind.String)
                    {
                        var value = choiceText.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) chunks.Add(value.Trim());
                    }
                }

                if (chunks.Count > 0)
                {
                    text = string.Join("\n", chunks);
                    return true;
                }
            }

            return false;
        }

        private static string NormalizePotentialSummaryJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            var trimmed = text.Trim();
            if (!(trimmed.StartsWith("{") || trimmed.StartsWith("[")))
                return trimmed;

            try
            {
                var dto = JsonSerializer.Deserialize<AiSummaryDto>(trimmed);
                var dtoText = FormatSummaryDto(dto);
                if (!string.IsNullOrWhiteSpace(dtoText))
                    return dtoText;

                using var doc = JsonDocument.Parse(trimmed);
                var fromSummary = doc.RootElement.ValueKind == JsonValueKind.Object
                    ? ExtractSummaryField(doc.RootElement)
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(fromSummary))
                    return fromSummary;
            }
            catch
            {
                // Keep original text if the model returned malformed JSON in a text field.
            }

            return trimmed;
        }

        public static string TruncateForDisplay(string s, int max = DefaultDisplayMax)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Length <= max) return s;
            return s.Substring(0, max).TrimEnd() + "\n\n[...resumen truncado. Usa 'Ver completo' para leer todo.]";
        }
    }
}
