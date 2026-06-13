using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Demlo.Application.DTOs;
using Demlo.Infrastructure.Persistence;
using Asp.Versioning;
using Demlo.Application.Common.Interfaces;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/kyc")]
public class KycController : ControllerBase
{
    private readonly DemloDbContext _context;
    private readonly IWalletService _walletService; // 1. Declare the private wallet field

    // 2. Inject IWalletService alongside the DbContext
    public KycController(DemloDbContext context, IWalletService walletService)
    {
        _context = context;
        _walletService = walletService;
    }

    [HttpPost("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SmileIdCallback([FromBody] SmileIdCallbackDto request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.PartnerParams == null || string.IsNullOrWhiteSpace(request.PartnerParams.UserId))
            {
                return BadRequest(new { error = "Missing tracking parameters in webhook context payload." });
            }

            if (!Guid.TryParse(request.PartnerParams.UserId, out var userId))
            {
                return BadRequest(new { error = "Invalid tracking user identifier formatting." });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user == null)
            {
                Console.WriteLine($"[SECURITY ALERT] KYC Webhook received for non-existent user identifier: {userId}");
                return NotFound(new { error = "Target user reference profile does not exist." });
            }

            if (request.ResultCodeGroup == 1)
            {
                Console.WriteLine($"[KYC SUCCESS] User identity verified via Smile ID for User: {userId}. Text: {request.ResultText}");

                // 3. AUTOMATED WALLET PROVISIONING DISPATCH (PL-43)
                Console.WriteLine($"[LEDGER TRIGGER] Provisioning secure double-entry financial ledger account for verified user: {userId}");
                var walletAllocationSuccess = await _walletService.ProvisionUserWalletAsync(userId, cancellationToken);

                if (!walletAllocationSuccess)
                {
                    Console.WriteLine($"[LEDGER ERROR CRITICAL] User identity passed, but database failed to allocate double-entry structures for User: {userId}");
                }
            }
            else
            {
                Console.WriteLine($"[KYC FAILURE] User identity rejected via Smile ID for User: {userId}. Code: {request.ResultCode}, Text: {request.ResultText}");
            }

            return Ok(new { status = "Webhook parsed and recorded successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL DEFAULT] KYC Webhook Parser Fault: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An internal error occurred while parsing verification states." });
        }
    }
}