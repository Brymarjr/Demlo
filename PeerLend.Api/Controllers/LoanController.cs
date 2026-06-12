using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Application.DTOs;
using System.Security.Claims;

namespace PeerLend.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/loans")]
public class LoanController : ControllerBase
{
    private readonly ILoanService _loanService;
    private readonly IUnderwritingEngine _underwritingEngine; // ◄ 1. Declare the private field

    // 2. Inject IUnderwritingEngine alongside the LoanService
    public LoanController(ILoanService loanService, IUnderwritingEngine underwritingEngine)
    {
        _loanService = loanService;
        _underwritingEngine = underwritingEngine;
    }

    [HttpPost("apply")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitLoanApplication([FromBody] LoanApplicationDto request, CancellationToken cancellationToken)
    {
        var userIdentifierClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdentifierClaim))
        {
            return BadRequest(new { error = "Unable to extract authenticated borrower context parameters from active session." });
        }

        var borrowerId = Guid.Parse(userIdentifierClaim);

        // 3. Formulate the loan asset inside the database (Starts at ApplicationSubmitted)
        var formulatedLoanAsset = await _loanService.SubmitApplicationAsync(
            borrowerId,
            request.PrincipalAmountKobo,
            request.InterestRateBps,
            request.TenorDays,
            cancellationToken
        );

        // 4. AUTOMATED RISK MATRIX TRIGGER (PL-51)
        // Fire the underwriting assessment instantly in the background to avoid blocking the user's response
        _ = Task.Run(() => _underwritingEngine.EvaluateLoanRiskAsync(formulatedLoanAsset.Id, CancellationToken.None), CancellationToken.None);

        return CreatedAtAction(
            nameof(SubmitLoanApplication),
            new { id = formulatedLoanAsset.Id },
            new { loanId = formulatedLoanAsset.Id, status = formulatedLoanAsset.Status.ToString(), message = "Loan application logged and queued into the automated underwriting pipeline successfully." }
        );
    }
}