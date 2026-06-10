using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeerLend.Application.DTOs;
using PeerLend.Infrastructure.Persistence;

namespace PeerLend.Api.Controllers;

[ApiController]
[Route("api/kyc")]
public class KycController : ControllerBase
{
    private readonly PeerLendDbContext _context;

    public KycController(PeerLendDbContext context)
    {
        _context = context;
    }

    // Listens for asynchronous identity validation responses from Smile ID (PL-30)
    [HttpPost("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SmileIdCallback([FromBody] SmileIdCallbackDto request, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Extract the unique tracking ID from the tracking parameters package
            if (request.PartnerParams == null || string.IsNullOrWhiteSpace(request.PartnerParams.UserId))
            {
                return BadRequest(new { error = "Missing tracking parameters in webhook context payload." });
            }

            if (!Guid.TryParse(request.PartnerParams.UserId, out var userId))
            {
                return BadRequest(new { error = "Invalid tracking user identifier formatting." });
            }

            // 2. Locate the matching user record inside our PostgreSQL engine
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user == null)
            {
                Console.WriteLine($"[SECURITY ALERT] KYC Webhook received for non-existent user identifier: {userId}");
                return NotFound(new { error = "Target user reference profile does not exist." });
            }

            // 3. Evaluate identity correctness rules based on Smile ID standard group rankings
            // Group Code 1 represents an absolute data match affirmation across statutory databases
            if (request.ResultCodeGroup == 1)
            {
                Console.WriteLine($"[KYC SUCCESS] User identity verified via Smile ID for User: {userId}. Text: {request.ResultText}");

                // NOTE: In upcoming migration cycles where user status flags are integrated,
                // we will toggle properties like IsKycVerified = true here.
            }
            else
            {
                Console.WriteLine($"[KYC FAILURE] User identity rejected via Smile ID for User: {userId}. Code: {request.ResultCode}, Text: {request.ResultText}");
            }

            // 4. Acknowledge receipt to Smile ID with an explicit HTTP 200 OK
            // This prevents their servers from retrying the webhook payload delivery
            return Ok(new { status = "Webhook parsed and recorded successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL DEFAULT] KYC Webhook Parser Fault: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An internal error occurred while parsing verification states." });
        }
    }
}
