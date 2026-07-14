using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PakkaHisaab.Api.Data;

namespace PakkaHisaab.Api.Auth;

public interface ITokenService
{
    (string Token, DateTime ExpiresAtUtc) CreateAccessToken(User user);
    string CreateRefreshToken();
}

public sealed class TokenService : ITokenService
{
    readonly IConfiguration _config;

    public TokenService(IConfiguration config) => _config = config;

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key not configured")));
        var expires = DateTime.UtcNow.AddMinutes(
            int.TryParse(_config["Jwt:AccessTokenMinutes"], out var m) ? m : 60);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.DisplayName)
            },
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public string CreateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
