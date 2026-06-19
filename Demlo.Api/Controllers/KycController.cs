using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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
    private readonly IWalletService _walletService;
    private readonly IKycService _kycService;

    public KycController(DemloDbContext context, IWalletService walletService, IKycService kycService)
    {
        _context = context;
        _walletService = walletService;
        _kycService = kycService;
    }

    // ──► ACTIVE ENDPOINT: Fetch Current KYC Status
    [HttpGet("status")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetKycStatus(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized(new { error = "Invalid token payload." });
        }

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null) return NotFound(new { error = "User identity not found." });

        return Ok(new { 
            userId = user.Id, 
            kycStatus = user.KycStatus // ◄ FIXED: Utilizing your existing Domain property
        });
    }

    // ──► ACTIVE ENDPOINT: Trigger Manual Retry (PRODUCTION READY)
    [HttpPost("retry")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> RetryKyc([FromBody] RetryKycDto request, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) return Unauthorized();

        var user = await _context.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (user == null) return NotFound();

        if (user.KycStatus == "Verified") 
        {
            return BadRequest(new { error = "User is already verified. Retry unnecessary." });
        }

        // 1. Fire the real request to the external gateway
        var success = await _kycService.SubmitKycAsync(user, request.IdType, request.IdNumber, cancellationToken);

        if (!success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Failed to communicate with the Smile ID gateway. Please try again." });
        }

        // 2. Safely lock the user's status back into a pending verification state
        user.KycStatus = "Pending";
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "KYC verification retry accepted and pushed to background processor." });
    }

    // ──► PASSIVE ENDPOINT: Smile ID Webhook
    [HttpPost("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SmileIdCallback([FromBody] SmileIdCallbackDto request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.PartnerParams == null || string.IsNullOrWhiteSpace(request.PartnerParams.UserId))
                return BadRequest(new { error = "Missing tracking parameters in webhook context payload." });

            if (!Guid.TryParse(request.PartnerParams.UserId, out var userId))
                return BadRequest(new { error = "Invalid tracking user identifier formatting." });

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user == null)
            {
                Console.WriteLine($"[SECURITY ALERT] KYC Webhook received for non-existent user: {userId}");
                return NotFound(new { error = "Target user reference profile does not exist." });
            }

            if (request.ResultCodeGroup == 1)
            {
                Console.WriteLine($"[KYC SUCCESS] User identity verified via Smile ID for User: {userId}.");

                // ──► FIXED: Patching the state save using your Domain's string property
                user.KycStatus = "Verified";
                _context.Users.Update(user);
                await _context.SaveChangesAsync(cancellationToken);

                var walletAllocationSuccess = await _walletService.ProvisionUserWalletAsync(userId, cancellationToken);
                if (!walletAllocationSuccess)
                    Console.WriteLine($"[LEDGER ERROR] Wallet allocation failed for User: {userId}");
            }
            else
            {
                Console.WriteLine($"[KYC FAILURE] Verification rejected. Code: {request.ResultCode}");
                
                // Track the failure so the frontend knows to ask the user to retry
                user.KycStatus = "Failed";
                _context.Users.Update(user);
                await _context.SaveChangesAsync(cancellationToken);
            }

            return Ok(new { status = "Webhook parsed and recorded successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL DEFAULT] KYC Webhook Parser Fault: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An internal error occurred." });
        }
    }
}