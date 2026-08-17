using DoubleMark.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoubleMark.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class AccountController : ControllerBase
{
    private readonly AccountDataService _data;

    public AccountController(AccountDataService data)
    {
        _data = data;
    }

    [HttpGet("me/profile")]
    public async Task<ActionResult<ProfileDto>> GetProfile(CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        var email = User.FindFirst("email")?.Value ?? User.Identity?.Name;
        return Ok(await _data.GetOrCreateProfileAsync(userId, email, ct));
    }

    [HttpPut("me/profile")]
    public async Task<ActionResult<ProfileDto>> UpdateProfile([FromBody] ProfileUpdateRequest request, CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        try
        {
            return Ok(await _data.UpdateProfileAsync(userId, request, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("me/subscription")]
    public async Task<ActionResult<SubscriptionDto?>> GetSubscription(CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        return Ok(await _data.GetSubscriptionAsync(userId, ct));
    }

    [HttpGet("me/payments")]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> GetPayments(CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        return Ok(await _data.GetPaymentsAsync(userId, ct));
    }

    [HttpGet("me/devices")]
    public async Task<ActionResult<IReadOnlyList<DeviceDto>>> GetDevices(CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        return Ok(await _data.GetDevicesAsync(userId, ct));
    }

    [HttpPost("me/devices")]
    public async Task<ActionResult<DeviceRegistrationResponse>> UpsertDevice(
        [FromBody] DeviceUpsertRequest request,
        CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        return Ok(await _data.UpsertDeviceAsync(userId, request, ct));
    }

    [HttpGet("me/templates")]
    public async Task<ActionResult<IReadOnlyList<TemplateDto>>> GetTemplates(CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        return Ok(await _data.GetTemplatesAsync(userId, ct));
    }

    [HttpPut("me/templates")]
    public async Task<ActionResult<TemplateDto>> UpsertTemplate([FromBody] TemplateUpsertRequest request, CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        try
        {
            return Ok(await _data.UpsertTemplateAsync(userId, request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpDelete("me/templates/{id:guid}")]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        await _data.DeleteTemplateAsync(userId, id, ct);
        return NoContent();
    }

    [HttpPost("me/templates/{id:guid}/default")]
    public async Task<IActionResult> SetDefaultTemplate(Guid id, CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        try
        {
            await _data.SetDefaultTemplateAsync(userId, id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("me/scan-history")]
    public async Task<ActionResult<IReadOnlyList<ScanHistoryDto>>> GetScanHistory(CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        return Ok(await _data.GetScanHistoryAsync(userId, ct));
    }

    [HttpGet("me/scan-history/count")]
    public async Task<ActionResult<object>> GetScanHistoryCount(CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        var count = await _data.GetScanHistoryCountAsync(userId, ct);
        return Ok(new { count, limit = 1000 });
    }

    [HttpPost("me/scan-history")]
    public async Task<ActionResult<ScanHistoryDto>> AddScanHistory(
        [FromBody] ScanHistoryCreateRequest request,
        CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        try
        {
            return Ok(await _data.AddScanHistoryAsync(userId, request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("me/scan-history/{id:guid}")]
    public async Task<IActionResult> DeleteScanHistory(Guid id, CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        await _data.DeleteScanHistoryItemAsync(userId, id, ct);
        return NoContent();
    }

    [HttpDelete("me/scan-history")]
    public async Task<IActionResult> ClearScanHistory(CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        await _data.ClearScanHistoryAsync(userId, ct);
        return NoContent();
    }
}
