using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AtmMonitor.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AtmMonitor.Api.Auth;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public static SymmetricSecurityKey CreateKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(o.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim("name", user.DisplayName),
            new Claim("role", user.Role.ToString()),
        };
        var token = new JwtSecurityToken(
            o.Issuer, o.Audience, claims, now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(CreateKey(o.SigningKey), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
