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
    private readonly IConfiguration _configuration;

    public WalletController(IWalletService walletService, IConfiguration configuration)
    {
        _walletService = walletService;
        _configuration = configuration;
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

    // The highly secure Paystack inbound listener
    [HttpPost("paystack-webhook")]
    [AllowAnonymous] // CRITICAL: Paystack servers do not have Demlo JWT tokens
    [ApiExplorerSettings(IgnoreApi = true)] // Hides this endpoint from your Swagger UI
    public async Task<IActionResult> PaystackWebhook()
    {
        var secretKey = _configuration["PaystackSettings:SecretKey"];
        var signatureHeader = Request.Headers["x-paystack-signature"].FirstOrDefault();

        // 1. Instantly reject if missing signature or configuration
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(secretKey))
            return BadRequest();

        // 2. Read the raw request body
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        // 3. Cryptographic Validation (HMAC-SHA512)
        using var hmac = new System.Security.Cryptography.HMACSHA512(System.Text.Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(body));
        var expectedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();

        // If the signatures don't match, an attacker is spoofing the payload
        if (expectedSignature != signatureHeader)
        {
            Console.WriteLine("[WEBHOOK CRITICAL] Invalid cryptographic signature detected. Possible spoofing attack.");
            return Unauthorized();
        }

        // 4. Safely parse the verified JSON payload
        try
        {
            var payload = System.Text.Json.Nodes.JsonNode.Parse(body);
            var eventName = payload?["event"]?.ToString();

            if (eventName == "charge.success")
            {
                var data = payload?["data"];
                var reference = data?["reference"]?.ToString();
                var amountKobo = data?["amount"]?.GetValue<long>() ?? 0;
                var status = data?["status"]?.ToString();

                if (status == "success" && !string.IsNullOrEmpty(reference))
                {
                    // Fire the wallet crediting sequence
                    await _walletService.ProcessPaystackWebhookAsync(reference, amountKobo, CancellationToken.None);
                }
            }

            // Always return 200 OK immediately so Paystack knows we got the message
            return Ok();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WEBHOOK PARSE FAULT] {ex.Message}");
            return StatusCode(500); // 500 tells Paystack to retry later
        }
    }

}