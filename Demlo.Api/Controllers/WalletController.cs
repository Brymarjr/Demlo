using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Asp.Versioning;
using Demlo.Application.Common.Interfaces;
using Demlo.Application.DTOs;
using System.Security.Claims;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/wallet")]
[Authorize] // ◄ FIXED: Secures all wallet interactions
public class WalletController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    [HttpGet("balance")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLiveBalance(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim)) return Unauthorized();

        var userId = Guid.Parse(userIdClaim);
        var clearBalanceKobo = await _walletService.GetWalletBalanceAsync(userId, cancellationToken);

        return Ok(new { userId = userId, currentBalanceKobo = clearBalanceKobo });
    }

    [HttpPost("transfer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InitiateWalletTransfer([FromBody] WalletTransferDto request, CancellationToken cancellationToken)
    {
        var senderIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(senderIdClaim)) return Unauthorized();

        var senderUserId = Guid.Parse(senderIdClaim);

        if (senderUserId == request.RecipientUserId)
        {
            return BadRequest(new { error = "Self-transfer executions are strictly prohibited across ledger architectures." });
        }

        var executionResult = await _walletService.TransferFundsAsync(senderUserId, request.RecipientUserId, request.AmountKobo, request.Description, cancellationToken);

        if (!executionResult)
        {
            return BadRequest(new { error = "Transfer pipeline execution rejected. Verify liquidity limits and recipient routing channels." });
        }

        return Ok(new { status = "Atomic ledger distribution completed and settled successfully." });
    }

    // ──► NEW: Wallet Funding via Paystack Checkout
    [HttpPost("deposit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InitializeDeposit([FromBody] DepositRequestDto request, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) return Unauthorized();

        try
        {
            var checkoutUrl = await _walletService.RequestDepositLinkAsync(userId, request.AmountKobo, cancellationToken);
            return Ok(new { 
                message = "Deposit session initialized.",
                authorizationUrl = checkoutUrl 
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ──► NEW: Wallet Cashout to Linked Bank Account
    [HttpPost("withdraw")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestWithdrawal([FromBody] WithdrawRequestDto request, CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) return Unauthorized();

        try
        {
            var success = await _walletService.RequestWithdrawalAsync(userId, request.AmountKobo, cancellationToken);
            
            if (!success) return BadRequest(new { error = "Withdrawal request failed validation." });

            return Ok(new { message = "Withdrawal request accepted and queued for processing." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}