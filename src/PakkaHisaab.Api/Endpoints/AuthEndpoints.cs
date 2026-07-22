using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Api.Auth;
using PakkaHisaab.Infrastructure.Auth;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Shared.Dtos;

namespace PakkaHisaab.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest req, AppDbContext db, ITokenService tokens) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || req.Password.Length < 8)
                return Results.BadRequest(new
                {
                    error = "Email required; password must be at least 8 characters.",
                    code = "INVALID_INPUT"
                });

            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict(new
                {
                    error = "An account with this email already exists.",
                    code = "EMAIL_TAKEN"
                });

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                DisplayName = req.DisplayName.Trim(),
                PhoneNumber = req.PhoneNumber,
                PasswordHash = PasswordHasher.Hash(req.Password),
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Users.Add(user);

            var auth = IssueTokens(user, tokens);
            await db.SaveChangesAsync();
            return Results.Ok(auth);
        });

        group.MapPost("/login", async (LoginRequest req, AppDbContext db, ITokenService tokens) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
                return Results.Unauthorized();

            var auth = IssueTokens(user, tokens);
            await db.SaveChangesAsync();
            return Results.Ok(auth);
        });

        group.MapPost("/refresh", async (RefreshRequest req, AppDbContext db, ITokenService tokens) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u =>
                u.RefreshToken == req.RefreshToken &&
                u.RefreshTokenExpiresAtUtc > DateTime.UtcNow);
            if (user is null) return Results.Unauthorized();

            var auth = IssueTokens(user, tokens); // rotation: old refresh token is replaced
            await db.SaveChangesAsync();
            return Results.Ok(auth);
        });
    }

    static AuthResponse IssueTokens(User user, ITokenService tokens)
    {
        var (access, expires) = tokens.CreateAccessToken(user);
        user.RefreshToken = tokens.CreateRefreshToken();
        user.RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(60);
        return new AuthResponse(user.Id, user.DisplayName, access, expires, user.RefreshToken);
    }
}
