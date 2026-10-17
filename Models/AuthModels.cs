using System;
using TinyDb.Attributes;

namespace JxTerminal.Server.Models;

/// <summary>A registered account stored on the server.</summary>
[Entity("users")]
public sealed class UserRecord
{
    [Id]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Server-wide dynamic configuration stored in database.</summary>
[Entity("server_settings")]
public sealed class ServerConfigRecord
{
    [Id]
    public string Id { get; set; } = "config";
    public bool AllowRegistration { get; set; } = true;
    public string RegistrationSecret { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Request body for creating a new account.</summary>
public sealed class RegisterRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Secret { get; set; }
}

/// <summary>Request body for signing in.</summary>
public sealed class LoginRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

/// <summary>Bearer token returned by register / login.</summary>
public sealed record AuthResponse(string Token, string Username, DateTime ExpiresAt, bool IsAdmin = false);

// ---- Admin DTOs ----

public sealed record AdminUserDto(
    string Id,
    string Username,
    bool IsAdmin,
    bool IsDisabled,
    DateTime CreatedAt,
    int BackupCount,
    long BackupSizeBytes);

public sealed class CreateUserRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool IsAdmin { get; set; }
}

public sealed class ResetPasswordRequest
{
    public string? NewPassword { get; set; }
}

public sealed class ChangePasswordRequest
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}

public sealed class UpdateConfigRequest
{
    public bool AllowRegistration { get; set; }
    public string? RegistrationSecret { get; set; }
}

public sealed record ServerStatsDto(
    int UserCount,
    int BackupCount,
    long TotalBackupSizeBytes,
    long DatabaseSizeBytes,
    bool AllowRegistration,
    bool HasRegistrationSecret,
    DateTime StartedAt,
    string Uptime);
