using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Nodus.API.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────

public sealed class AdminTokenRequest
{
    public string AdminSecret { get; set; } = string.Empty;  // Matches JWT:SecretKey in appsettings
    public string EventId     { get; set; } = string.Empty;
}

public sealed record AdminTokenResponse(string Token, DateTime ExpiresAt);

// ── Endpoint Registration ─────────────────────────────────────────────────

public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        // POST /api/auth/token
        // Admin app calls this to obtain a short-lived JWT for syncing data.
        // Authentication is via the shared AdminSecret set in Render env vars.
        app.MapPost("/api/auth/token", (AdminTokenRequest req, IConfiguration config) =>
        {
            var secret = config["Jwt:SecretKey"] ?? string.Empty;
            if (string.IsNullOrEmpty(secret) || req.AdminSecret != secret)
                return Results.Unauthorized();

            var issuer   = config["Jwt:Issuer"]   ?? "nodus-api";
            var audience = config["Jwt:Audience"] ?? "nodus-admin";
            var hours    = int.TryParse(config["Jwt:ExpiresHours"], out var h) ? h : 24;

            var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expiry = DateTime.UtcNow.AddHours(hours);

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "nodus-admin"),
                new Claim("event_id", req.EventId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(issuer, audience, claims, expires: expiry, signingCredentials: creds);
            var tokenStr = new JwtSecurityTokenHandler().WriteToken(token);

            return Results.Ok(new AdminTokenResponse(tokenStr, expiry));
        })
        .AllowAnonymous()
        .WithName("GetAdminToken")
        .WithSummary("Admin app obtains a JWT by presenting the shared AdminSecret.")
        .Produces<AdminTokenResponse>()
        .ProducesProblem(401);
    }
}
