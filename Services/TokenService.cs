using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace JxTerminal.Server.Services;

/// <summary>
/// Issues and validates short-lived JWT bearer tokens for authenticated users.
/// </summary>
public sealed class TokenService
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SymmetricSecurityKey _key;
    private readonly string _issuer;
    private readonly string _audience;

    public TimeSpan Lifetime { get; }

    public TokenValidationParameters ValidationParameters { get; }

    public TokenService(string secret, string issuer, string audience, TimeSpan lifetime)
    {
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _issuer = issuer;
        _audience = audience;
        Lifetime = lifetime;

        ValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role
        };
    }

    public string CreateToken(string userId, string username, bool isAdmin = false)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        if (isAdmin)
        {
            claims.Add(new(ClaimTypes.Role, "Admin"));
            claims.Add(new("role", "admin"));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _issuer,
            Audience = _audience,
            Expires = DateTime.UtcNow.Add(Lifetime),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
        };
        return _handler.CreateToken(descriptor);
    }
}
