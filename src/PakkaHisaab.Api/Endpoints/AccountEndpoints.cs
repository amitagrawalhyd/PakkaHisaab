using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Api.Auth;
using PakkaHisaab.Api.Data;
using PakkaHisaab.Shared.Dtos;

namespace PakkaHisaab.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // App Store / Play Store compliance: "Delete My Account & Data".
        // Hard-deletes the user row and every replicated record — not a soft delete.
        app.MapDelete("/account", async ([FromBody] DeleteAccountRequest req, ClaimsPrincipal principal,
                AppDbContext db, ILoggerFactory lf) =>
            {
                var userId = principal.GetUserId();
                if (userId is null) return Results.Unauthorized();

                if (!string.Equals(req.Confirmation, "DELETE", StringComparison.Ordinal))
                    return Results.BadRequest(new { error = "Confirmation must be the literal string DELETE." });

                var user = await db.Users.FindAsync(userId.Value);
                if (user is null) return Results.NotFound();
                if (!PasswordHasher.Verify(req.Password, user.PasswordHash))
                    return Results.Unauthorized();

                await using var tx = await db.Database.BeginTransactionAsync();
                await db.Helpers.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.Attendance.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.LedgerEntries.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.Settlements.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await db.SyncBatches.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                db.Users.Remove(user);
                await db.SaveChangesAsync();
                await tx.CommitAsync();

                lf.CreateLogger("Account").LogInformation(
                    "Account {UserId} and all data erased at user request.", userId);
                return Results.NoContent();
            })
            .WithTags("Account")
            .RequireAuthorization();
    }
}
