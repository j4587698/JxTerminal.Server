namespace JxTerminal.Server.Models;

// ---- Response DTOs ----
// Native AOT disables reflection-based JSON, so every response type must be a
// concrete type that is listed on a JsonSerializerContext. Anonymous types can
// not be referenced by [JsonSerializable] and would fail at runtime, therefore
// all endpoint payloads are declared here.

/// <summary>Generic error payload: <c>{ "error": "..." }</c>.</summary>
public sealed record ErrorResponse(string Error);

/// <summary>Generic acknowledgement payload: <c>{ "status": "ok" }</c>.</summary>
public sealed record OkResponse(string Status);

/// <summary>GET /api/health</summary>
public sealed record HealthResponse(string Status, DateTime Time);

/// <summary>GET /api/public/status</summary>
public sealed record PublicStatusResponse(
    int UserCount,
    bool AllowRegistration,
    bool HasRegistrationSecret,
    string Version);

/// <summary>GET /api/auth/me</summary>
public sealed record MeResponse(string Id, string Username, bool IsAdmin, DateTime CreatedAt);

/// <summary>GET /api/sync/status</summary>
public sealed record SyncStatusResponse(
    bool HasData,
    DateTime? UpdatedAt,
    string Id,
    string DeviceName,
    string AppVersion);

/// <summary>POST /api/sync/ result.</summary>
public sealed record SyncResultResponse(
    string Id,
    string DeviceName,
    string Comment,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string AppVersion,
    long SizeBytes);

/// <summary>POST /api/admin/users result.</summary>
public sealed record UserCreatedResponse(string Id, string Username, bool IsAdmin);

/// <summary>PUT /api/admin/users/{id}/toggle-status result.</summary>
public sealed record ToggleStatusResponse(string Id, bool IsDisabled);

/// <summary>GET|PUT /api/admin/config</summary>
public sealed record ConfigResponse(bool AllowRegistration, string RegistrationSecret);
