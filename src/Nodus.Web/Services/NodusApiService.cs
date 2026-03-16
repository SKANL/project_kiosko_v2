using System.Net.Http.Json;
using System.Text.Json;
using Nodus.Web.DTOs;

namespace Nodus.Web.Services;

/// <summary>
/// Wraps all calls to the Admin's local Kestrel API (http://[LOCAL_IP]:5000).
/// The server base URL is passed in at call time from the query-string parameter
/// ?server=192.168.1.x:5000 so this service is reusable across any event.
/// </summary>
public sealed class NodusApiService
{
    private readonly HttpClient _http;

    public NodusApiService(HttpClient http) => _http = http;

    // ── Event Info ─────────────────────────────────────────────────────────

    public async Task<EventInfoDto?> GetCurrentEventAsync(string serverBase)
    {
        try
        {
            return await _http.GetFromJsonAsync<EventInfoDto>($"http://{serverBase}/api/event/current");
        }
        catch { return null; }
    }

    // ── Project Registration ────────────────────────────────────────────────

    public async Task<(RegisterProjectResponse? Response, string? Error)> RegisterProjectAsync(
        string serverBase, RegisterProjectRequest request)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"http://{serverBase}/api/projects", request);
            if (response.IsSuccessStatusCode)
            {
                var dto = await response.Content.ReadFromJsonAsync<RegisterProjectResponse>();
                return (dto, null);
            }
            if ((int)response.StatusCode == 409)
                return (null, "registration_closed");

            var detail = await response.Content.ReadAsStringAsync();
            return (null, $"Error {(int)response.StatusCode}: {detail}");
        }
        catch (Exception ex)
        {
            return (null, $"No se pudo contactar con el servidor del evento: {ex.Message}");
        }
    }

    // ── Edit Registration ───────────────────────────────────────────────────

    public async Task<ProjectEditDto?> GetProjectForEditAsync(string serverBase, string editToken)
    {
        try
        {
            return await _http.GetFromJsonAsync<ProjectEditDto>(
                $"http://{serverBase}/api/projects/edit/{editToken}");
        }
        catch { return null; }
    }

    public async Task<(bool Success, string? Error)> UpdateProjectAsync(
        string serverBase, string editToken, UpdateProjectRequest request)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"http://{serverBase}/api/projects/edit/{editToken}", request);

            if (response.IsSuccessStatusCode) return (true, null);
            if ((int)response.StatusCode == 410) return (false, "event_closed");
            return (false, $"Error {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"No se pudo contactar con el servidor: {ex.Message}");
        }
    }
}
