using System.Security.Claims;
using JxTerminal.Server.Models;
using JxTerminal.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using TinyDb.Core;

var startTime = DateTime.UtcNow;

var builder = WebApplication.CreateBuilder(args);

// Runtime data root. Override via --data-dir, appsettings or env: "Backup:Directory" / Backup__Directory
var dataDirectory = builder.Configuration["data-dir"] ?? builder.Configuration["Backup:Directory"];

if (string.IsNullOrWhiteSpace(dataDirectory))
{
    dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
}
Directory.CreateDirectory(dataDirectory);

var dbPath = Path.Combine(dataDirectory, "server.db");

// Check for CLI maintenance commands before starting web server
if (IsCliCommand(args))
{
    TinyDbEngine? cliDb = null;
    try
    {
        cliDb = new TinyDbEngine(dbPath);
    }
    catch (IOException)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Error] Cannot access database '{dbPath}' because it is in use by another process.");
        Console.WriteLine("If JxTerminal.Server is currently running, please stop it first before executing CLI maintenance commands.");
        Console.ResetColor();
        Environment.ExitCode = 1;
        return;
    }

    try
    {
        await HandleCliCommandAsync(args, cliDb, dataDirectory);
    }
    finally
    {
        cliDb.Dispose();
    }
    return;
}

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
var configService = new ServerConfigService(db, builder.Configuration);

builder.Services.AddSingleton(db);
builder.Services.AddSingleton(new BackupStore(db));
builder.Services.AddSingleton(new UserStore(db));
builder.Services.AddSingleton(tokenService);
builder.Services.AddSingleton(configService);

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
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

// Public server metadata for UI onboarding and registration requirements
api.MapGet("/public/status", async (UserStore users, ServerConfigService cfg) =>
{
    var config = await cfg.GetConfigAsync();
    var userCount = await users.GetCountAsync();
    return Results.Ok(new
    {
        userCount,
        allowRegistration = config.AllowRegistration,
        hasRegistrationSecret = !string.IsNullOrEmpty(config.RegistrationSecret),
        version = typeof(UserStore).Assembly.GetName().Version != null
            ? $"{typeof(UserStore).Assembly.GetName().Version!.Major}.{typeof(UserStore).Assembly.GetName().Version!.Minor}"
            : "0.1"
    });
});

// Helper for extracting current user ID
static string? CurrentUserId(HttpContext http) => http.User.FindFirstValue(JwtRegisteredClaimNames.Sub);

// ---- Account endpoints ----
var auth = api.MapGroup("/auth");

auth.MapPost("/register", async (RegisterRequest request, UserStore users, TokenService tokens, ServerConfigService cfg) =>
{
    var config = await cfg.GetConfigAsync();
    var currentCount = await users.GetCountAsync();

    // The very first user is always allowed to register (becomes Super Admin)
    if (currentCount > 0)
    {
        if (!config.AllowRegistration)
        {
            return Results.BadRequest(new { error = "Registration is closed on this server." });
        }

        if (!string.IsNullOrEmpty(config.RegistrationSecret))
        {
            if (string.IsNullOrWhiteSpace(request.Secret) || request.Secret != config.RegistrationSecret)
            {
                return Results.BadRequest(new { error = "Invalid registration secret / invite code." });
            }
        }
    }

    try
    {
        var user = await users.RegisterAsync(request.Username ?? "", request.Password ?? "");
        return Results.Ok(new AuthResponse(
            tokens.CreateToken(user.Id, user.Username, user.IsAdmin),
            user.Username,
            DateTime.UtcNow.Add(tokens.Lifetime),
            user.IsAdmin));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

auth.MapPost("/login", async (LoginRequest request, UserStore users, TokenService tokens) =>
{
    var (user, error) = await users.ValidateAsync(request.Username ?? "", request.Password ?? "");
    if (user == null)
    {
        return Results.BadRequest(new { error = error ?? "Invalid username or password." });
    }

    return Results.Ok(new AuthResponse(
        tokens.CreateToken(user.Id, user.Username, user.IsAdmin),
        user.Username,
        DateTime.UtcNow.Add(tokens.Lifetime),
        user.IsAdmin));
});

auth.MapGet("/me", async (HttpContext http, UserStore users) =>
{
    var userId = CurrentUserId(http);
    var user = string.IsNullOrEmpty(userId) ? null : await users.GetByIdAsync(userId);
    return user == null
        ? Results.Unauthorized()
        : Results.Ok(new
        {
            id = user.Id,
            username = user.Username,
            isAdmin = user.IsAdmin,
            createdAt = user.CreatedAt
        });
}).RequireAuthorization();

auth.MapPost("/change-password", async (ChangePasswordRequest request, HttpContext http, UserStore users, TokenService tokens) =>
{
    var userId = CurrentUserId(http);
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    try
    {
        var success = await users.ChangePasswordAsync(userId, request.CurrentPassword ?? "", request.NewPassword ?? "");
        if (!success) return Results.NotFound();

        var user = await users.GetByIdAsync(userId);
        return Results.Ok(new AuthResponse(
            tokens.CreateToken(user!.Id, user.Username, user.IsAdmin),
            user.Username,
            DateTime.UtcNow.Add(tokens.Lifetime),
            user.IsAdmin));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

// ---- Backup endpoints (require a valid bearer token) ----
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

// ---- Sync endpoints (require a valid bearer token) ----
var sync = api.MapGroup("/sync").RequireAuthorization();

sync.MapGet("/status", async (HttpContext http, BackupStore store) =>
{
    var userId = CurrentUserId(http);
    if (userId == null) return Results.Unauthorized();

    var record = await store.GetLatestAsync(userId);
    if (record == null)
    {
        return Results.Ok(new { hasData = false, updatedAt = (DateTime?)null, id = "", deviceName = "", appVersion = "" });
    }
    return Results.Ok(new
    {
        hasData = true,
        updatedAt = (DateTime?)record.CreatedAt,
        id = record.Id,
        deviceName = record.DeviceName,
        appVersion = record.AppVersion
    });
});

sync.MapGet("/latest", async (HttpContext http, BackupStore store) =>
{
    var userId = CurrentUserId(http);
    if (userId == null) return Results.Unauthorized();

    var record = await store.GetLatestAsync(userId);
    return record == null ? Results.NotFound() : Results.Ok(record);
});

sync.MapPost("/", async (BackupUploadRequest request, HttpContext http, BackupStore store) =>
{
    var userId = CurrentUserId(http);
    if (userId == null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.SnapshotJson))
    {
        return Results.BadRequest(new { error = "snapshotJson is required." });
    }

    var record = await store.CreateAsync(userId, request.DeviceName, request.Comment ?? "AutoSync", request.SnapshotJson, request.AppVersion);
    await store.PruneAsync(userId, maxKeep: 10);
    return Results.Ok(new
    {
        id = record.Id,
        deviceName = record.DeviceName,
        comment = record.Comment,
        createdAt = record.CreatedAt,
        updatedAt = record.CreatedAt,
        appVersion = record.AppVersion,
        sizeBytes = System.Text.Encoding.UTF8.GetByteCount(record.SnapshotJson)
    });
});

// ---- Admin endpoints (require Admin role) ----
var admin = api.MapGroup("/admin").RequireAuthorization();

admin.AddEndpointFilter(async (context, next) =>
{
    var user = context.HttpContext.User;
    var isAdmin = user.IsInRole("Admin") || user.HasClaim("role", "admin");
    if (!isAdmin)
    {
        return Results.Forbid();
    }
    return await next(context);
});

admin.MapGet("/stats", async (UserStore users, BackupStore backupStore, ServerConfigService cfg) =>
{
    var config = await cfg.GetConfigAsync();
    var userCount = await users.GetCountAsync();
    var backupCount = await backupStore.GetTotalCountAsync();
    var totalBackupBytes = await backupStore.GetTotalSizeBytesAsync();

    long dbSize = 0;
    try
    {
        if (File.Exists(dbPath)) dbSize = new FileInfo(dbPath).Length;
    }
    catch { }

    var uptime = DateTime.UtcNow - startTime;
    string uptimeStr = $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";

    return Results.Ok(new ServerStatsDto(
        userCount,
        backupCount,
        totalBackupBytes,
        dbSize,
        config.AllowRegistration,
        !string.IsNullOrEmpty(config.RegistrationSecret),
        startTime,
        uptimeStr));
});

admin.MapGet("/users", async (UserStore users, BackupStore backupStore) =>
{
    var allUsers = await users.GetAllAsync();
    var list = new List<AdminUserDto>();
    foreach (var u in allUsers)
    {
        var (count, sizeBytes) = await backupStore.GetStatsForUserAsync(u.Id);
        list.Add(new AdminUserDto(
            u.Id,
            u.Username,
            u.IsAdmin,
            u.IsDisabled,
            u.CreatedAt,
            count,
            sizeBytes));
    }
    return Results.Ok(list);
});

admin.MapPost("/users", async (CreateUserRequest request, UserStore users) =>
{
    try
    {
        var user = await users.RegisterAsync(request.Username ?? "", request.Password ?? "", request.IsAdmin);
        return Results.Ok(new { id = user.Id, username = user.Username, isAdmin = user.IsAdmin });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

admin.MapDelete("/users/{id}", async (string id, HttpContext http, UserStore users, BackupStore backupStore) =>
{
    var currentId = CurrentUserId(http);
    if (currentId == id)
    {
        return Results.BadRequest(new { error = "Cannot delete your own admin account." });
    }

    var user = await users.GetByIdAsync(id);
    if (user == null) return Results.NotFound();

    await backupStore.DeleteAllForUserAsync(id);
    await users.DeleteAsync(id);

    return Results.NoContent();
});

admin.MapPut("/users/{id}/toggle-status", async (string id, HttpContext http, UserStore users) =>
{
    var currentId = CurrentUserId(http);
    if (currentId == id)
    {
        return Results.BadRequest(new { error = "Cannot disable your own admin account." });
    }

    var user = await users.GetByIdAsync(id);
    if (user == null) return Results.NotFound();

    await users.SetDisabledAsync(id, !user.IsDisabled);
    return Results.Ok(new { id, isDisabled = !user.IsDisabled });
});

admin.MapPut("/users/{id}/reset-password", async (string id, ResetPasswordRequest request, UserStore users) =>
{
    try
    {
        var success = await users.ResetPasswordAsync(id, request.NewPassword ?? "");
        return success ? Results.Ok(new { status = "ok" }) : Results.NotFound();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

admin.MapGet("/config", async (ServerConfigService cfg) =>
{
    var config = await cfg.GetConfigAsync();
    return Results.Ok(new
    {
        allowRegistration = config.AllowRegistration,
        registrationSecret = config.RegistrationSecret
    });
});

admin.MapPut("/config", async (UpdateConfigRequest request, ServerConfigService cfg) =>
{
    var updated = await cfg.UpdateConfigAsync(request.AllowRegistration, request.RegistrationSecret);
    return Results.Ok(new
    {
        allowRegistration = updated.AllowRegistration,
        registrationSecret = updated.RegistrationSecret
    });
});

app.Run();

static bool IsCliCommand(string[] args)
{
    return args.Any(a => string.Equals(a, "--list-users", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--list-backups", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--reset-password", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--create-admin", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--set-admin", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--delete-user", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--server-help", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase));
}

static async Task HandleCliCommandAsync(string[] args, TinyDbEngine db, string dataDirectory)
{
    int IndexOf(string name) => Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    bool hasHelp = IndexOf("--help") >= 0 || IndexOf("-h") >= 0 || IndexOf("--server-help") >= 0;
    bool hasListUsers = IndexOf("--list-users") >= 0;
    bool hasListBackups = IndexOf("--list-backups") >= 0;
    int resetPwdIdx = IndexOf("--reset-password");
    int createAdminIdx = IndexOf("--create-admin");
    int setAdminIdx = IndexOf("--set-admin");
    int deleteUserIdx = IndexOf("--delete-user");

    var users = new UserStore(db);

    if (hasHelp)
    {
        Console.WriteLine(@"JxTerminal Server CLI Administration:
  --list-users                                 List all registered users, roles, and status
  --list-backups                               List all cloud backups and snapshots
  --reset-password <username> <new-password>   Reset password for the specified user
  --create-admin <username> <password>         Create a new Administrator account
  --set-admin <username> [true|false]          Promote or demote user administrator privilege
  --delete-user <username>                     Delete a user and their cloud backups
  --Backup:Directory <path> / --data-dir <path> Specify database directory (default: ./data)
");
        return;
    }

    if (hasListBackups)
    {
        Console.WriteLine($"[JxTerminal Server] Database directory: {dataDirectory}");
        var col = db.GetCollection<BackupRecord>();
        var allBackups = await col.FindAllAsync();
        Console.WriteLine($"Found {allBackups.Count} backup(s):");
        foreach (var b in allBackups)
        {
            Console.WriteLine($"ID: {b.Id} | User: {b.UserId} | Device: {b.DeviceName} | Created: {b.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            if (!string.IsNullOrEmpty(b.SnapshotJson))
            {
                var preview = b.SnapshotJson.Length > 1500 ? b.SnapshotJson.Substring(0, 1500) : b.SnapshotJson;
                Console.WriteLine($"  JSON: {preview}...\n");
            }
        }
        return;
    }

    if (hasListUsers)
    {
        Console.WriteLine($"[JxTerminal Server] Database directory: {dataDirectory}");
        var allUsers = await users.GetAllAsync();
        if (allUsers.Count == 0)
        {
            Console.WriteLine("No registered users found. The first user to register will automatically become Super Admin.");
        }
        else
        {
            Console.WriteLine($"Found {allUsers.Count} user(s):");
            Console.WriteLine(new string('-', 85));
            Console.WriteLine($"{"Username",-20} {"Role",-10} {"Status",-10} {"Created At (UTC)",-22} {"ID"}");
            Console.WriteLine(new string('-', 85));
            foreach (var u in allUsers)
            {
                var role = u.IsAdmin ? "Admin" : "User";
                var status = u.IsDisabled ? "Disabled" : "Active";
                Console.WriteLine($"{u.Username,-20} {role,-10} {status,-10} {u.CreatedAt:yyyy-MM-dd HH:mm:ss}   {u.Id}");
            }
            Console.WriteLine(new string('-', 85));
        }
        return;
    }

    if (resetPwdIdx >= 0)
    {
        if (resetPwdIdx + 2 >= args.Length || args[resetPwdIdx + 1].StartsWith('-') || args[resetPwdIdx + 2].StartsWith('-'))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: --reset-password requires <username> and <new-password>.");
            Console.ResetColor();
            Console.WriteLine("Usage: dotnet JxTerminal.Server.dll --reset-password <username> <new-password>");
            Environment.ExitCode = 1;
            return;
        }

        var username = args[resetPwdIdx + 1].Trim();
        var newPassword = args[resetPwdIdx + 2];

        try
        {
            var user = await users.GetByUsernameAsync(username);
            if (user == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: User '{username}' was not found in database ({dataDirectory}).");
                Console.ResetColor();
                Environment.ExitCode = 1;
                return;
            }

            var success = await users.ResetPasswordAsync(user.Id, newPassword);
            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Success] Password for user '{user.Username}' (ID: {user.Id}) has been successfully reset!");
                Console.ResetColor();
                Console.WriteLine("Notice: If using Cloud Sync with end-to-end encryption, log in with the new password on an existing client device to automatically re-encrypt and refresh the cloud snapshot.");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Failed to reset password for user '{username}'.");
                Console.ResetColor();
                Environment.ExitCode = 1;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            Environment.ExitCode = 1;
        }
        return;
    }

    if (createAdminIdx >= 0)
    {
        if (createAdminIdx + 2 >= args.Length || args[createAdminIdx + 1].StartsWith('-') || args[createAdminIdx + 2].StartsWith('-'))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: --create-admin requires <username> and <password>.");
            Console.ResetColor();
            Console.WriteLine("Usage: dotnet JxTerminal.Server.dll --create-admin <username> <password>");
            Environment.ExitCode = 1;
            return;
        }

        var username = args[createAdminIdx + 1].Trim();
        var password = args[createAdminIdx + 2];

        try
        {
            var user = await users.RegisterAsync(username, password, forceAdmin: true);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Success] Admin account '{user.Username}' (ID: {user.Id}) created successfully.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            Environment.ExitCode = 1;
        }
        return;
    }

    if (setAdminIdx >= 0)
    {
        if (setAdminIdx + 1 >= args.Length || args[setAdminIdx + 1].StartsWith('-'))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: --set-admin requires <username> [true|false].");
            Console.ResetColor();
            Console.WriteLine("Usage: dotnet JxTerminal.Server.dll --set-admin <username> [true|false]");
            Environment.ExitCode = 1;
            return;
        }

        var username = args[setAdminIdx + 1].Trim();
        bool isAdmin = true;
        if (setAdminIdx + 2 < args.Length && !args[setAdminIdx + 2].StartsWith('-'))
        {
            if (bool.TryParse(args[setAdminIdx + 2], out var parsed))
            {
                isAdmin = parsed;
            }
        }

        var user = await users.GetByUsernameAsync(username);
        if (user == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: User '{username}' was not found.");
            Console.ResetColor();
            Environment.ExitCode = 1;
            return;
        }

        await users.SetAdminByUsernameAsync(username, isAdmin);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Success] User '{user.Username}' admin status updated to: {isAdmin}.");
        Console.ResetColor();
        return;
    }

    if (deleteUserIdx >= 0)
    {
        if (deleteUserIdx + 1 >= args.Length || args[deleteUserIdx + 1].StartsWith('-'))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: --delete-user requires <username>.");
            Console.ResetColor();
            Console.WriteLine("Usage: dotnet JxTerminal.Server.dll --delete-user <username>");
            Environment.ExitCode = 1;
            return;
        }

        var username = args[deleteUserIdx + 1].Trim();
        var user = await users.GetByUsernameAsync(username);
        if (user == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: User '{username}' was not found.");
            Console.ResetColor();
            Environment.ExitCode = 1;
            return;
        }

        var backupStore = new BackupStore(db);
        await backupStore.DeleteAllForUserAsync(user.Id);
        await users.DeleteAsync(user.Id);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Success] User '{user.Username}' (ID: {user.Id}) and associated cloud data were deleted successfully.");
        Console.ResetColor();
    }
}

