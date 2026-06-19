using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Demlo.Application.Common.Interfaces;
using Demlo.Application.DTOs;
using Demlo.Infrastructure.Persistence;
using Asp.Versioning;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/bank-accounts")]
[Authorize] // ◄ Must be logged in
public class BankAccountController : ControllerBase
{
    private readonly IOpenBankingService _openBankingService;
    private readonly DemloDbContext _context;

    public BankAccountController(IOpenBankingService openBankingService, DemloDbContext context)
    {
        _openBankingService = openBankingService;
        _context = context;
    }

    [HttpPost("link")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LinkAccount([FromBody] LinkBankAccountDto request, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) return Unauthorized();

        var user = await _context.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (user == null) return NotFound(new { error = "User identity not found." });

        try
        {
            // 1. Execute the server-to-server handshake to get the permanent Mono Account ID
            var accountId = await _openBankingService.ExchangeAuthCodeForAccountIdAsync(request.AuthCode, cancellationToken);

            // 2. Route the save operation based on the user's architectural role
            if (user.Role == Domain.Enums.UserRole.Borrower)
            {
                var profile = await _context.BorrowerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
                if (profile == null) return BadRequest(new { error = "Borrower profile not found. Complete Tier-1 onboarding first." });
                
                profile.BankAccountId = accountId;
                _context.BorrowerProfiles.Update(profile);
            }
            else if (user.Role == Domain.Enums.UserRole.Lender)
            {
                var profile = await _context.LenderProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
                if (profile == null) return BadRequest(new { error = "Lender profile not found. Complete Tier-1 onboarding first." });
                
                profile.BankAccountId = accountId;
                _context.LenderProfiles.Update(profile);
            }
            else
            {
                return BadRequest(new { error = "Administrative accounts cannot link retail banking profiles." });
            }

            // 3. Commit to PostgreSQL
            await _context.SaveChangesAsync(cancellationToken);

            // 4. Trigger the initial asynchronous data sync in the background without blocking the HTTP response
            _ = _openBankingService.SynchronizeAccountTelemetryAsync(user, accountId, CancellationToken.None);

            return Ok(new { 
                message = "Commercial bank account linked successfully.", 
                accountId = accountId 
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL BANKING FAULT]: {ex.Message}");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Communication with the Open Banking provider failed." });
        }
    }
}