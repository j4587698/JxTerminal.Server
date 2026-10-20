using System.Text.Json.Serialization;
using JxTerminal.Server.Models;

namespace JxTerminal.Server.Serialization;

/// <summary>
/// Source-generated JSON metadata. Required because this app is published with
/// <c>PublishAot=true</c>, which turns off the reflection-based serializer
/// (<c>JsonSerializer.IsReflectionEnabledByDefault == false</c>). Without a
/// registered <see cref="JsonSerializerContext"/>, minimal-API body binding and
/// <c>Results.Ok(...)</c> throw:
/// <c>NotSupportedException: JsonTypeInfo metadata for type '...' was not provided
/// by TypeInfoResolver of type '[]'</c>.
///
/// Every type that is read from a request body or written to a response must be
/// listed below. The options mirror the ASP.NET Core web defaults (camelCase
/// names, case-insensitive matching) so the wire format does not change.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Default)]
// ---- Request bodies ----
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(ChangePasswordRequest))]
[JsonSerializable(typeof(BackupUploadRequest))]
[JsonSerializable(typeof(CreateUserRequest))]
[JsonSerializable(typeof(ResetPasswordRequest))]
[JsonSerializable(typeof(UpdateConfigRequest))]
// ---- Response payloads ----
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(BackupInfo))]
[JsonSerializable(typeof(List<BackupInfo>))]
// The backups list endpoint returns IReadOnlyList<BackupInfo>; Results.Ok<T> serializes
// using the *static* type of T, so the interface type needs metadata of its own.
[JsonSerializable(typeof(IReadOnlyList<BackupInfo>))]
[JsonSerializable(typeof(BackupRecord))]
[JsonSerializable(typeof(AdminUserDto))]
[JsonSerializable(typeof(List<AdminUserDto>))]
[JsonSerializable(typeof(ServerStatsDto))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(OkResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(PublicStatusResponse))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(SyncStatusResponse))]
[JsonSerializable(typeof(SyncResultResponse))]
[JsonSerializable(typeof(UserCreatedResponse))]
[JsonSerializable(typeof(ToggleStatusResponse))]
[JsonSerializable(typeof(ConfigResponse))]
public sealed partial class AppJsonContext : JsonSerializerContext
{
}
