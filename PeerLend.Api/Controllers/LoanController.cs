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

    public LoanController(ILoanService loanService)
    {
        _loanService = loanService;
    }

    // Intercepts inbound borrower terms and triggers the state machine initialization (PL-48)
    [HttpPost("apply")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitLoanApplication([FromBody] LoanApplicationDto request, CancellationToken cancellationToken)
    {
        // Safely pull the user's identity out of the token context, bypassing client-side tampering
        var userIdentifierClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdentifierClaim))
        {
            return BadRequest(new { error = "Unable to extract authenticated borrower context parameters from active session." });
        }

        var borrowerId = Guid.Parse(userIdentifierClaim);

        // Dispatch variables directly down to our underlying infrastructure state engine
        var formulatedLoanAsset = await _loanService.SubmitApplicationAsync(
            borrowerId,
            request.PrincipalAmountKobo,
            request.InterestRateBps,
            request.TenorDays,
            cancellationToken
        );

        return CreatedAtAction(
            nameof(SubmitLoanApplication),
            new { id = formulatedLoanAsset.Id },
            new { loanId = formulatedLoanAsset.Id, status = formulatedLoanAsset.Status.ToString(), message = "Loan application logged and queued into the underwriting pipeline successfully." }
        );
    }
}