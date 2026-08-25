using DoubleMark.Api.Auth;
using DoubleMark.Api.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoubleMark.Api.Controllers;

[ApiController]
[Route("api/billing")]
public sealed class BillingController : ControllerBase
{
    private readonly BillingService _billing;

    public BillingController(BillingService billing)
    {
        _billing = billing;
    }

    [HttpGet("plans")]
    [AllowAnonymous]
    public ActionResult<object> Plans([FromQuery] string period = "monthly")
    {
        return Ok(new { period, plans = _billing.ListPlans(period) });
    }

    [HttpPost("checkout")]
    [Authorize]
    public async Task<ActionResult<CheckoutResponse>> Checkout(
        [FromBody] CheckoutRequest request,
        CancellationToken ct)
    {
        try
        {
            var userId = AccountDataService.GetUserId(User);
            var result = await _billing.CreateCheckoutAsync(userId, request.PlanId, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("payments/{id:guid}")]
    [Authorize]
    public async Task<ActionResult<PaymentStatusDto>> PaymentStatus(Guid id, CancellationToken ct)
    {
        try
        {
            var userId = AccountDataService.GetUserId(User);
            var payment = await _billing.GetPaymentAsync(userId, id, ct);
            return payment == null ? NotFound(new { error = "Платёж не найден." }) : Ok(payment);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = ex.Message });
        }
    }

    [HttpPost("sandbox/confirm")]
    [Authorize]
    public async Task<ActionResult<PaymentStatusDto>> SandboxConfirm(
        [FromQuery] Guid paymentId,
        CancellationToken ct)
    {
        try
        {
            var userId = AccountDataService.GetUserId(User);
            return Ok(await _billing.ConfirmSandboxAsync(userId, paymentId, ct));
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

    [HttpGet("sandbox/confirm")]
    [Authorize]
    public Task<ActionResult<PaymentStatusDto>> SandboxConfirmGet(
        [FromQuery] Guid paymentId,
        CancellationToken ct) =>
        SandboxConfirm(paymentId, ct);

    [HttpPost("webhook/alpha")]
    [AllowAnonymous]
    public async Task<IActionResult> AlphaWebhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        AlphaWebhookRequest? body = null;
        try
        {
            body = System.Text.Json.JsonSerializer.Deserialize<AlphaWebhookRequest>(
                string.IsNullOrWhiteSpace(raw) ? "{}" : raw,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            // form-urlencoded fallback
        }

        body ??= new AlphaWebhookRequest(
            Request.Form["orderNumber"].ToString(),
            Request.Form["orderId"].ToString(),
            Request.Form["status"].ToString(),
            Request.Form["operation"].ToString(),
            Request.Form["checksum"].ToString());

        var signature = Request.Headers["X-Alpha-Signature"].FirstOrDefault() ?? body.Checksum;
        try
        {
            await _billing.HandleWebhookAsync(body, raw, signature, ct);
            return Ok(new { ok = true });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Неверная подпись." });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
