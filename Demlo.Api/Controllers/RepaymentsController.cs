using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Demlo.Application.Common.Interfaces;
using Demlo.Application.DTOs;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/repayments")]
public class RepaymentsController : ControllerBase
{
    private readonly IRepaymentService _repaymentService;

    public RepaymentsController(IRepaymentService repaymentService)
    {
        _repaymentService = repaymentService;
    }

    // Pulls real-time remaining liabilities calculated dynamically out of the transaction tables (PL-55)
    [HttpGet("outstanding/{loanId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOutstandingDebt([FromRoute] Guid loanId, CancellationToken cancellationToken)
    {
        var remainingLiabilityKobo = await _repaymentService.CalculateOutstandingBalanceAsync(loanId, cancellationToken);
        return Ok(new { loanId = loanId, outstandingBalanceKobo = remainingLiabilityKobo });
    }

    // Dispatches a balance-validated double-entry recovery ledger sequence (PL-55)
    [HttpPost("submit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitRepayment([FromBody] RepaymentRequestDto request, CancellationToken cancellationToken)
    {
        var processResult = await _repaymentService.ProcessRepaymentAsync(
            request.LoanId,
            request.AmountKobo,
            cancellationToken
        );

        if (!processResult)
        {
            return BadRequest(new { error = "Repayment processing rejected. Check ledger liquidity parameters or active loan state." });
        }

        return Ok(new { status = "Settlement processed, verified, and applied to balance matrix cleanly." });
    }
}