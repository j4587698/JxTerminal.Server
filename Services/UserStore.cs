using System;
using System.Collections.Generic;
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

    public async Task<int> GetCountAsync()
    {
        var col = _db.GetCollection<UserRecord>();
        var all = await col.FindAllAsync();
        return all.Count;
    }

    public async Task<IReadOnlyList<UserRecord>> GetAllAsync()
    {
        var col = _db.GetCollection<UserRecord>();
        var all = await col.FindAllAsync();
        return all.OrderBy(u => u.CreatedAt).ToList();
    }

    /// <summary>
    /// Registers a new account. If it is the first account ever created, it automatically
    /// becomes an Admin.
    /// </summary>
    public async Task<UserRecord> RegisterAsync(string username, string password, bool? forceAdmin = null)
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
        var existing = await col.FindAsync(u => u.Username.ToLower() == name.ToLower());
        if (existing.Any())
        {
            throw new ArgumentException("Username is already taken.");
        }

        int count = await GetCountAsync();
        bool isAdmin = forceAdmin ?? (count == 0);

        var user = new UserRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Username = name,
            PasswordHash = _passwordHasher.HashPassword(new UserRecord { Username = name }, password),
            IsAdmin = isAdmin,
            IsDisabled = false,
            CreatedAt = DateTime.UtcNow
        };

        await col.InsertAsync(user);
        return user;
    }

    /// <summary>
    /// Validates credentials; returns (user, errorMessage).
    /// </summary>
    public async Task<(UserRecord? User, string? Error)> ValidateAsync(string username, string password)
    {
        var name = username?.Trim() ?? "";
        var col = _db.GetCollection<UserRecord>();
        var users = await col.FindAsync(u => u.Username.ToLower() == name.ToLower());
        var user = users.FirstOrDefault();
        if (user == null)
        {
            return (null, "Invalid username or password.");
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password ?? "");
        if (result != PasswordVerificationResult.Success)
        {
            return (null, "Invalid username or password.");
        }

        if (user.IsDisabled)
        {
            return (null, "This account has been disabled by an administrator.");
        }

        return (user, null);
    }

    public async Task<UserRecord?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var col = _db.GetCollection<UserRecord>();
        return await col.FindByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var col = _db.GetCollection<UserRecord>();
        var deleted = await col.DeleteManyAsync(u => u.Id == id);
        return deleted > 0;
    }

    public async Task<bool> SetDisabledAsync(string id, bool disabled)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var col = _db.GetCollection<UserRecord>();
        var user = await col.FindByIdAsync(id);
        if (user == null) return false;

        user.IsDisabled = disabled;
        await col.UpdateAsync(user);
        return true;
    }

    public async Task<bool> ResetPasswordAsync(string id, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            throw new ArgumentException("Password must be at least 6 characters.");
        }

        var col = _db.GetCollection<UserRecord>();
        var user = await col.FindByIdAsync(id);
        if (user == null) return false;

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        await col.UpdateAsync(user);
        return true;
    }

    public async Task<bool> ChangePasswordAsync(string id, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            throw new ArgumentException("Password must be at least 6 characters.");
        }

        var col = _db.GetCollection<UserRecord>();
        var user = await col.FindByIdAsync(id);
        if (user == null) return false;

        var verify = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? "");
        if (verify != PasswordVerificationResult.Success)
        {
            throw new ArgumentException("Current password is incorrect.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        await col.UpdateAsync(user);
        return true;
    }

    public async Task<UserRecord?> GetByUsernameAsync(string username)
    {
        var name = username?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) return null;
        var col = _db.GetCollection<UserRecord>();
        var users = await col.FindAsync(u => u.Username.ToLower() == name.ToLower());
        return users.FirstOrDefault();
    }

    public async Task<bool> ResetPasswordByUsernameAsync(string username, string newPassword)
    {
        var user = await GetByUsernameAsync(username);
        if (user == null) return false;
        return await ResetPasswordAsync(user.Id, newPassword);
    }

    public async Task<bool> SetAdminByUsernameAsync(string username, bool isAdmin)
    {
        var user = await GetByUsernameAsync(username);
        if (user == null) return false;
        var col = _db.GetCollection<UserRecord>();
        user.IsAdmin = isAdmin;
        await col.UpdateAsync(user);
        return true;
    }

    public async Task<bool> DeleteByUsernameAsync(string username)
    {
        var user = await GetByUsernameAsync(username);
        if (user == null) return false;
        return await DeleteAsync(user.Id);
    }
}

