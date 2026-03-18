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

// ── JSON Configuration ──────────────────────────────────────────────────
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

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
// Explicitly allow Vercel + localhost — also keeps AllowAnyOrigin as fallback
builder.Services.AddCors(opts =>
{
    opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(
                "https://project-kiosko-v2.vercel.app",
                "http://localhost:3000",
                "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
    opts.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

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

// ── Diagnostic Endpoints ──────────────────────────────────────────────────
app.MapGet("/", () => $"Nodus API is alive at {DateTime.UtcNow} UTC");
app.MapGet("/ping", () => "pong");

// ── Endpoints ─────────────────────────────────────────────────────────────
AuthEndpoints.Map(app);
PublicEndpoints.Map(app);
SyncEndpoints.Map(app);
MediaEndpoints.Map(app);
ResultsEndpoints.Map(app);

// ── Health check ──────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }))
   .AllowAnonymous()
   .WithName("HealthCheck")
   .WithSummary("Returns 200 OK — used by Render/Back4App health checks.");

// ── Fallback Logging ──────────────────────────────────────────────────────
app.MapFallback(async context =>
{
    Console.WriteLine($"[404 DEBUG] {context.Request.Method} {context.Request.Path}");
    context.Response.StatusCode = 404;
    await context.Response.WriteAsync($"Nodus API: Route '{context.Request.Path}' not found.");
});

app.Run();
