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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Request body for creating a new account.</summary>
public sealed class RegisterRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

/// <summary>Request body for signing in.</summary>
public sealed class LoginRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

/// <summary>Bearer token returned by register / login.</summary>
public sealed record AuthResponse(string Token, string Username, DateTime ExpiresAt);
