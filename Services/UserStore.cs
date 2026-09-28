using System;
using System.Linq;
using System.Threading.Tasks;
using JxTerminal.Server.Models;
using Microsoft.AspNetCore.Identity;
using TinyDb.Core;

namespace JxTerminal.Server.Services;

/// <summary>
/// Persists registered accounts in TinyDb. Passwords are hashed with ASP.NET Core's
/// PBKDF2-based PasswordHasher (never stored in plain text).
/// </summary>
public sealed class UserStore
{
    private readonly TinyDbEngine _db;
    private readonly PasswordHasher<UserRecord> _passwordHasher = new();

    public UserStore(TinyDbEngine db)
    {
        _db = db;
    }

    /// <summary>
    /// Registers a new account. Throws ArgumentException on invalid/duplicate input.
    /// </summary>
    public async Task<UserRecord> RegisterAsync(string username, string password)
    {
        var name = username?.Trim() ?? "";
        if (name.Length < 3)
        {
            throw new ArgumentException("Username must be at least 3 characters.");
        }
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            throw new ArgumentException("Password must be at least 6 characters.");
        }

        var col = _db.GetCollection<UserRecord>();
        var allUsers = await col.FindAsync(u => u.Username.ToLower() == name.ToLower());
        if (allUsers.Any())
        {
            throw new ArgumentException("Username is already taken.");
        }

        var user = new UserRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = name,
            PasswordHash = _passwordHasher.HashPassword(new UserRecord { Username = name }, password),
            CreatedAt = DateTime.UtcNow
        };

        await col.InsertAsync(user);
        return user;
    }

    /// <summary>
    /// Validates credentials; returns the account on success, otherwise null.
    /// </summary>
    public async Task<UserRecord?> ValidateAsync(string username, string password)
    {
        var name = username?.Trim() ?? "";
        var col = _db.GetCollection<UserRecord>();
        var users = await col.FindAsync(u => u.Username.ToLower() == name.ToLower());
        var user = users.FirstOrDefault();
        if (user == null) return null;

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
        return result == PasswordVerificationResult.Success ? user : null;
    }

    public async Task<UserRecord?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var col = _db.GetCollection<UserRecord>();
        return await col.FindByIdAsync(id);
    }
}
