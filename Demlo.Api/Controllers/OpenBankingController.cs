using Microsoft.AspNetCore.Mvc;
using Demlo.Application.DTOs;
using Asp.Versioning;
using Microsoft.Extensions.Configuration;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")] // Locks this route handler structure to Version 1 paths
[Route("api/v{version:apiVersion}/openbanking")]
public class OpenBankingController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public OpenBankingController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // Intercepts and validates telemetry events dispatched by the Mono background network (PL-37)
    [HttpPost("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult MonoWebhookCallback([FromBody] MonoWebhookDto request)
    {
        try
        {
            // 1. Extract the secure authentication webhook verification secret from HTTP request headers
            if (!Request.Headers.TryGetValue("mono-webhook-secret", out var inboundSecret))
            {
                Console.WriteLine("[SECURITY WARNING] Inbound Mono Webhook rejected: Missing 'mono-webhook-secret' header mapping.");
                return Unauthorized(new { error = "Request authorization signature constraints missing." });
            }

            var localSecret = _configuration["MonoSettings:WebhookSecret"]
                ?? throw new InvalidOperationException("Local platform Mono Webhook Secret signature is unconfigured.");

            // 2. Perform security verification to confirm the request originated from Mono
            if (inboundSecret != localSecret)
            {
                Console.WriteLine("[SECURITY ALERT] Counterfeit Mono Webhook signature mismatch intercepted!");
                return Unauthorized(new { error = "Invalid or corrupted request authorization signature tokens." });
            }

            // 3. Verify that the inner structural transaction payloads are initialized
            if (request.Data == null || request.Data.Account == null)
            {
                return BadRequest(new { error = "Malformed or unparseable webhook event metadata configuration parameters." });
            }

            var eventType = request.Event;
            var monoAccountId = request.Data.Account.Id;
            var accountBalanceKobo = request.Data.Account.Balance;

            Console.WriteLine($"[OPEN BANKING WEBHOOK] Event type '{eventType}' processed for Mono Account: {monoAccountId}. Current balance converted: {accountBalanceKobo} kobo.");

            // NOTE: In upcoming risk analysis and ledger update modules, 
            // we will pass this telemetry data directly down to our credit evaluation engine handlers.

            // 4. Return HTTP 200 OK to acknowledge event delivery and halt server retries
            return Ok(new { status = "Telemetry event processed and verified successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL DEFAULT] Open Banking Webhook Gateway Fault: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An internal error occurred while parsing connection telemetry states." });
        }
    }
}
