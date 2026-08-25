using DoubleMark.Api.Admin;
using DoubleMark.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoubleMark.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly AdminService _admin;

    public AdminController(AdminService admin)
    {
        _admin = admin;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<AdminOverviewDto>> Overview(CancellationToken ct)
    {
        var rejected = await RejectIfNotAdmin(ct);
        if (rejected != null)
            return rejected;
        return Ok(await _admin.GetOverviewAsync(ct));
    }

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<AdminUserDto>>> Users(CancellationToken ct)
    {
        var rejected = await RejectIfNotAdmin(ct);
        if (rejected != null)
            return rejected;
        return Ok(await _admin.GetUsersAsync(ct));
    }

    [HttpGet("organizations")]
    public async Task<ActionResult<IReadOnlyList<AdminOrganizationDto>>> Organizations(CancellationToken ct)
    {
        var rejected = await RejectIfNotAdmin(ct);
        if (rejected != null)
            return rejected;
        return Ok(await _admin.GetOrganizationsAsync(ct));
    }

    [HttpGet("payments")]
    public async Task<ActionResult<IReadOnlyList<AdminPaymentRowDto>>> Payments(CancellationToken ct)
    {
        var rejected = await RejectIfNotAdmin(ct);
        if (rejected != null)
            return rejected;
        return Ok(await _admin.GetPaymentsAsync(ct));
    }

    [HttpGet("devices")]
    public async Task<ActionResult<IReadOnlyList<AdminDeviceRowDto>>> Devices(CancellationToken ct)
    {
        var rejected = await RejectIfNotAdmin(ct);
        if (rejected != null)
            return rejected;
        return Ok(await _admin.GetDevicesAsync(ct));
    }

    [HttpPatch("users/{id:guid}/role")]
    public async Task<ActionResult<AdminUserDto>> SetRole(
        Guid id,
        [FromBody] AdminSetRoleRequest request,
        CancellationToken ct)
    {
        var rejected = await RejectIfNotAdmin(ct);
        if (rejected != null)
            return rejected;

        try
        {
            return Ok(await _admin.SetRoleAsync(id, request.Role, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<ActionResult<object>> ResetPassword(Guid id, CancellationToken ct)
    {
        var rejected = await RejectIfNotAdmin(ct);
        if (rejected != null)
            return rejected;

        try
        {
            var result = await _admin.SendPasswordResetAsync(id, ct);
            return Ok(new { ok = result.Ok, message = result.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("users/{id:guid}/resend-confirmation")]
    public async Task<ActionResult<object>> ResendConfirmation(Guid id, CancellationToken ct)
    {
        var rejected = await RejectIfNotAdmin(ct);
        if (rejected != null)
            return rejected;

        try
        {
            var result = await _admin.ResendConfirmationAsync(id, ct);
            return Ok(new { ok = result.Ok, message = result.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken ct)
    {
        var rejected = await RejectIfNotAdmin(ct);
        if (rejected != null)
            return rejected;

        try
        {
            var actorId = AccountDataService.GetUserId(User);
            await _admin.DeleteUserAsync(id, actorId, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private async Task<ActionResult?> RejectIfNotAdmin(CancellationToken ct)
    {
        var userId = AccountDataService.GetUserId(User);
        if (await _admin.IsAdminAsync(userId, ct))
            return null;
        return StatusCode(StatusCodes.Status403Forbidden, new { error = "admin access required" });
    }
}
