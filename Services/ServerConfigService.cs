using System;
using System.Threading.Tasks;
using JxTerminal.Server.Models;
using Microsoft.Extensions.Configuration;
using TinyDb.Core;

namespace JxTerminal.Server.Services;

/// <summary>
/// Manages persistent server settings in TinyDb with fallbacks to appsettings / environment variables.
/// </summary>
public sealed class ServerConfigService
{
    private readonly TinyDbEngine _db;
    private readonly IConfiguration _config;
    private ServerConfigRecord? _cached;

    public ServerConfigService(TinyDbEngine db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<ServerConfigRecord> GetConfigAsync()
    {
        if (_cached != null) return _cached;

        var col = _db.GetCollection<ServerConfigRecord>();
        var record = await col.FindByIdAsync("config");
        if (record == null)
        {
            record = new ServerConfigRecord
            {
                Id = "config",
                AllowRegistration = _config.GetValue("Auth:AllowRegistration", true),
                RegistrationSecret = _config["Auth:RegistrationSecret"] ?? "",
                UpdatedAt = DateTime.UtcNow
            };
            await col.InsertAsync(record);
        }

        _cached = record;
        return record;
    }

    public async Task<ServerConfigRecord> UpdateConfigAsync(bool allowRegistration, string? registrationSecret)
    {
        var col = _db.GetCollection<ServerConfigRecord>();
        var record = await col.FindByIdAsync("config") ?? new ServerConfigRecord { Id = "config" };

        record.AllowRegistration = allowRegistration;
        record.RegistrationSecret = (registrationSecret ?? "").Trim();
        record.UpdatedAt = DateTime.UtcNow;

        await col.UpsertAsync(record);
        _cached = record;
        return record;
    }
}
