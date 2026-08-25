using DoubleMark.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoubleMark.Api.Controllers;

public sealed record CookieConsentRequest(
    string ConsentId,
    string ConsentVersion,
    DateTime Timestamp,
    bool Necessary,
    bool Analytics,
    bool Functional,
    bool Marketing,
    string Action);

[ApiController]
[Route("api/consent")]
public sealed class ConsentController : ControllerBase
{
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "grant",
        "update",
        "revoke"
    };

    private readonly AppDbContext _db;

    public ConsentController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Record([FromBody] CookieConsentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ConsentId) || request.ConsentId.Length > 64)
            return BadRequest(new { error = "consentId is required" });
        if (string.IsNullOrWhiteSpace(request.ConsentVersion) || request.ConsentVersion.Length > 32)
            return BadRequest(new { error = "consentVersion is required" });
        if (!AllowedActions.Contains(request.Action ?? ""))
            return BadRequest(new { error = "action is invalid" });

        _db.CookieConsentEvents.Add(new CookieConsentEventEntity
        {
            Id = Guid.NewGuid(),
            ConsentId = request.ConsentId.Trim(),
            ConsentVersion = request.ConsentVersion.Trim(),
            ClientTimestampUtc = DateTime.SpecifyKind(request.Timestamp, DateTimeKind.Utc),
            ReceivedAtUtc = DateTime.UtcNow,
            Necessary = true,
            Analytics = request.Analytics,
            Functional = request.Functional,
            Marketing = request.Marketing,
            Action = request.Action.Trim().ToLowerInvariant()
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
