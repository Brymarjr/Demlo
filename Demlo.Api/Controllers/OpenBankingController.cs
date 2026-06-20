using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Demlo.Application.DTOs;
using Asp.Versioning;
using Microsoft.Extensions.Configuration;
using Demlo.Application.Common.Interfaces;
using Demlo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")] // Locks this route handler structure to Version 1 paths
[Route("api/v{version:apiVersion}/openbanking")]
public class OpenBankingController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly DemloDbContext _context; 
    private readonly IOpenBankingService _openBankingService; 

    public OpenBankingController(IConfiguration configuration, DemloDbContext context, IOpenBankingService openBankingService)
    {
        _configuration = configuration;
        _context = context;
        _openBankingService = openBankingService;
    }

    // Finishes Open Banking loop and saves withdrawal details
    [HttpPost("bank-link")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LinkBankAccount([FromBody] LinkBankDto request, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.MonoCode))
            return BadRequest(new { error = "Mono authorization code is required." });

        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. Swap Code for permanent Account ID
            string accountId = await _openBankingService.ExchangeAuthCodeForAccountIdAsync(request.MonoCode, cancellationToken);

            // 2. Fetch real-world Bank Name and Account Number
            var (accountNumber, bankName) = await _openBankingService.GetAccountDetailsAsync(accountId, cancellationToken);

            if (string.IsNullOrEmpty(accountNumber))
                return BadRequest(new { error = "Mono could not resolve a valid account number for this bank connection." });

            // 3. Save to the appropriate Profile based on User Role
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user == null) return NotFound();

            if (user.Role == Domain.Enums.UserRole.Borrower)
            {
                var profile = await _context.BorrowerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
                if (profile != null)
                {
                    profile.BankAccountNumber = accountNumber;
                    profile.BankName = bankName;
                    _context.BorrowerProfiles.Update(profile);
                }
            }
            else if (user.Role == Domain.Enums.UserRole.Lender)
            {
                var profile = await _context.LenderProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
                if (profile != null)
                {
                    profile.BankAccountNumber = accountNumber;
                    profile.BankName = bankName;
                    _context.LenderProfiles.Update(profile);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            Console.WriteLine($"[OPEN BANKING SUCCESS] Bank linked for User {userId}. Bank: {bankName}, Acc: *******{accountNumber[^4..]}");
            
            return Ok(new { message = "Bank account linked securely. Withdrawals are now enabled." });
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[BANK LINK FAULT] {ex.Message}");
            return BadRequest(new { error = "Failed to synchronize bank account with Open Banking provider." });
        }
    }

    // Intercepts and validates telemetry events dispatched by the Mono background network
    [HttpPost("callback")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult MonoWebhookCallback([FromBody] MonoWebhookDto request)
    {
        try
        {
            if (!Request.Headers.TryGetValue("mono-webhook-secret", out var inboundSecret))
            {
                Console.WriteLine("[SECURITY WARNING] Inbound Mono Webhook rejected: Missing 'mono-webhook-secret' header mapping.");
                return Unauthorized(new { error = "Request authorization signature constraints missing." });
            }

            var localSecret = _configuration["MonoSettings:WebhookSecret"]
                ?? throw new InvalidOperationException("Local platform Mono Webhook Secret signature is unconfigured.");

            if (inboundSecret != localSecret)
            {
                Console.WriteLine("[SECURITY ALERT] Counterfeit Mono Webhook signature mismatch intercepted!");
                return Unauthorized(new { error = "Invalid or corrupted request authorization signature tokens." });
            }

            if (request.Data == null || request.Data.Account == null)
            {
                return BadRequest(new { error = "Malformed or unparseable webhook event metadata configuration parameters." });
            }

            var eventType = request.Event;
            var monoAccountId = request.Data.Account.Id;
            var accountBalanceKobo = request.Data.Account.Balance;

            Console.WriteLine($"[OPEN BANKING WEBHOOK] Event type '{eventType}' processed for Mono Account: {monoAccountId}. Current balance converted: {accountBalanceKobo} kobo.");

            return Ok(new { status = "Telemetry event processed and verified successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL DEFAULT] Open Banking Webhook Gateway Fault: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An internal error occurred while parsing connection telemetry states." });
        }
    }
}