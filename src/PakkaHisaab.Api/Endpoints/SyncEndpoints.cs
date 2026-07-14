using System.Security.Claims;
using PakkaHisaab.Api.Services;
using PakkaHisaab.Shared.Sync;

namespace PakkaHisaab.Api.Endpoints;

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sync").WithTags("Sync").RequireAuthorization();

        // Idempotent delta push (outbox drain from the MAUI Shiny job).
        group.MapPost("/push", async (SyncPushRequest req, ClaimsPrincipal principal,
            ISyncService sync, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();
            if (req.ClientBatchId == Guid.Empty)
                return Results.BadRequest(new { error = "ClientBatchId is required for idempotency." });

            return Results.Ok(await sync.PushAsync(userId.Value, req, ct));
        });

        // Incremental pull since watermark.
        group.MapPost("/pull", async (SyncPullRequest req, ClaimsPrincipal principal,
            ISyncService sync, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null) return Results.Unauthorized();

            return Results.Ok(await sync.PullAsync(userId.Value, req, ct));
        });
    }

    public static Guid? GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
