using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Api.Data;
using PakkaHisaab.Shared.Domain;

namespace PakkaHisaab.Api.Endpoints;

public record ParseLedgerRequest(string Text);

public static class AiEndpoints
{
    public static void MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ai").WithTags("AI").RequireAuthorization();

        // Voice-to-Ledger: same shared parser the client runs offline (DRY).
        // Hosted here for thin clients / future LLM upgrade behind the same contract.
        group.MapPost("/parse", async (ParseLedgerRequest req, ClaimsPrincipal principal,
            AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var names = await db.Helpers
                .Where(h => h.UserId == userId && !h.IsDeleted)
                .Select(h => h.Name)
                .ToListAsync(ct);

            return Results.Ok(VoiceLedgerParser.Parse(req.Text, names));
        });

        // Smart Leave Forecasting for a helper, computed server-side from full history.
        group.MapGet("/forecast/{helperId:guid}", async (Guid helperId, ClaimsPrincipal principal,
            AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            var history = await db.Attendance
                .Where(a => a.UserId == userId && a.HelperId == helperId && !a.IsDeleted)
                .Select(a => new Shared.Dtos.AttendanceDto
                {
                    Id = a.Id, HelperId = a.HelperId, Date = a.Date, Status = a.Status
                })
                .ToListAsync(ct);

            var forecast = LeaveForecaster.Forecast(history, DateTime.UtcNow);
            return forecast is null ? Results.NoContent() : Results.Ok(forecast);
        });
    }
}
