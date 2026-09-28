using System.Security.Claims;
using JxTerminal.Server.Models;
using JxTerminal.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using TinyDb.Core;

var builder = WebApplication.CreateBuilder(args);

// Runtime data root. Override via appsettings or env: "Backup:Directory" / Backup__Directory
var dataDirectory = builder.Configuration["Backup:Directory"];
if (string.IsNullOrWhiteSpace(dataDirectory))
{
    dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
}
Directory.CreateDirectory(dataDirectory);

var dbPath = Path.Combine(dataDirectory, "server.db");
var db = new TinyDbEngine(dbPath);

// JWT signing secret: prefer config ("Auth:JwtSecret"), otherwise a persisted random key so
// tokens survive restarts.
var jwtSecret = builder.Configuration["Auth:JwtSecret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    var secretFile = Path.Combine(dataDirectory, "jwt.secret");
    if (!File.Exists(secretFile))
    {
        File.WriteAllText(secretFile, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
    }
    jwtSecret = File.ReadAllText(secretFile).Trim();
}

var tokenService = new TokenService(jwtSecret, "JxTerminal.Server", "JxTerminalClient", TimeSpan.FromDays(7));

builder.Services.AddSingleton(db);
builder.Services.AddSingleton(new BackupStore(db));
builder.Services.AddSingleton(new UserStore(db));
builder.Services.AddSingleton(tokenService);

// The client is a desktop app, but allowing CORS keeps the API testable from a browser.
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = tokenService.ValidationParameters;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

// ---- Account endpoints (anonymous) ----
var auth = api.MapGroup("/auth");

auth.MapPost("/register", async (RegisterRequest request, UserStore users, TokenService tokens, IConfiguration config) =>
{
    var allowRegistration = config.GetValue("Auth:AllowRegistration", true);
    if (!allowRegistration)
    {
        return Results.BadRequest(new { error = "Registration is disabled on this server." });
    }

    try
    {
        var user = await users.RegisterAsync(request.Username ?? "", request.Password ?? "");
        return Results.Ok(new AuthResponse(
            tokens.CreateToken(user.Id, user.Username),
            user.Username,
            DateTime.UtcNow.Add(tokens.Lifetime)));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

auth.MapPost("/login", async (LoginRequest request, UserStore users, TokenService tokens) =>
{
    var user = await users.ValidateAsync(request.Username ?? "", request.Password ?? "");
    if (user == null)
    {
        return Results.Unauthorized();
    }
    return Results.Ok(new AuthResponse(
        tokens.CreateToken(user.Id, user.Username),
        user.Username,
        DateTime.UtcNow.Add(tokens.Lifetime)));
});

auth.MapGet("/me", async (HttpContext http, UserStore users) =>
{
    var userId = CurrentUserId(http);
    var user = string.IsNullOrEmpty(userId) ? null : await users.GetByIdAsync(userId);
    return user == null
        ? Results.Unauthorized()
        : Results.Ok(new { id = user.Id, username = user.Username, createdAt = user.CreatedAt });
}).RequireAuthorization();

// ---- Backup endpoints (require a valid bearer token) ----
static string? CurrentUserId(HttpContext http) => http.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

var backups = api.MapGroup("/backups").RequireAuthorization();

backups.MapGet("/", async (HttpContext http, BackupStore store) =>
{
    var userId = CurrentUserId(http);
    return userId == null ? Results.Unauthorized() : Results.Ok(await store.ListAsync(userId));
});

backups.MapGet("/{id}", async (string id, HttpContext http, BackupStore store) =>
{
    var userId = CurrentUserId(http);
    if (userId == null) return Results.Unauthorized();

    var record = await store.GetAsync(id, userId);
    return record == null ? Results.NotFound() : Results.Ok(record);
});

backups.MapPost("/", async (BackupUploadRequest request, HttpContext http, BackupStore store) =>
{
    var userId = CurrentUserId(http);
    if (userId == null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.SnapshotJson))
    {
        return Results.BadRequest(new { error = "snapshotJson is required." });
    }

    var record = await store.CreateAsync(userId, request.DeviceName, request.Comment, request.SnapshotJson, request.AppVersion);
    return Results.Created($"/api/backups/{record.Id}", record.ToInfo());
});

backups.MapDelete("/{id}", async (string id, HttpContext http, BackupStore store) =>
{
    var userId = CurrentUserId(http);
    if (userId == null) return Results.Unauthorized();

    return await store.DeleteAsync(id, userId) ? Results.NoContent() : Results.NotFound();
});

app.Run();
