using System.Text;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Nodus.API.Endpoints;
using Nodus.API.Services;
using Scalar.AspNetCore;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

// ── MongoDB ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<MongoDbService>();
builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
    var mongo = sp.GetRequiredService<MongoDbService>();
    return mongo.Events.Database;   // returns the same IMongoDatabase instance
});

// ── JWT Authentication ───────────────────────────────────────────────────
var jwtKey     = builder.Configuration["Jwt:SecretKey"] ?? string.Empty;
var jwtIssuer  = builder.Configuration["Jwt:Issuer"]   ?? "nodus-api";
var jwtAud     = builder.Configuration["Jwt:Audience"] ?? "nodus-admin";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtIssuer,
            ValidAudience            = jwtAud,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// ── CORS ─────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()));

// ── OpenAPI / Scalar ─────────────────────────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opts =>
    {
        opts.Title = "Nodus API";
        opts.Theme = ScalarTheme.Solarized;
    });
}

// ── Endpoints ─────────────────────────────────────────────────────────────
AuthEndpoints.Map(app);
SyncEndpoints.Map(app);
MediaEndpoints.Map(app);
ResultsEndpoints.Map(app);

// ── Health check ──────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }))
   .AllowAnonymous()
   .WithName("HealthCheck")
   .WithSummary("Returns 200 OK — used by Render health checks.");

app.Run();
