using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Application.DTOs;
using System.Security.Claims;

namespace PeerLend.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/wallet")]
public class WalletController : ControllerBase
{
    private readonly IWalletService _walletService;

    public WalletController(IWalletService walletService)
    {
        _walletService = walletService;
    }

    // Pulls the current real-time mathematical balance calculated directly from ledger transactions (PL-45)
    [HttpGet("balance")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLiveBalance(CancellationToken cancellationToken)
    {
        // Extract the user identity claim string directly out of the incoming authenticated security token context
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Dynamic fallback fallback hook context for testing environments before full JWT authorization middleware activation
        if (string.IsNullOrEmpty(userIdClaim))
        {
            return BadRequest(new { error = "Unable to resolve active session authentication token properties." });
        }

        var userId = Guid.Parse(userIdClaim);
        var clearBalanceKobo = await _walletService.GetWalletBalanceAsync(userId, cancellationToken);

        return Ok(new { userId = userId, currentBalanceKobo = clearBalanceKobo });
    }

    // Dispatches an atomic, balance-validated peer-to-peer ledger fund movement (PL-45)
    [HttpPost("transfer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InitiateWalletTransfer([FromBody] WalletTransferDto request, CancellationToken cancellationToken)
    {
        var senderIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(senderIdClaim))
        {
            return BadRequest(new { error = "Unable to resolve active sender session properties." });
        }

        var senderUserId = Guid.Parse(senderIdClaim);

        if (senderUserId == request.RecipientUserId)
        {
            return BadRequest(new { error = "Self-transfer executions are strictly prohibited across ledger architectures." });
        }

        var executionResult = await _walletService.TransferFundsAsync(
            senderUserId,
            request.RecipientUserId,
            request.AmountKobo,
            request.Description,
            cancellationToken
        );

        if (!executionResult)
        {
            return BadRequest(new { error = "Transfer pipeline execution rejected. Verify liquidity limits and recipient routing channels." });
        }

        return Ok(new { status = "Atomic ledger distribution completed and settled successfully." });
    }
}