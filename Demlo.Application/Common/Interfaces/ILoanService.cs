using Demlo.Domain.Entities;
using Demlo.Domain.Enums;

namespace Demlo.Application.Common.Interfaces;

// Outlines core use cases for the Loan Application and State Machine engine.
// Enforces Section 4.2 and Section 4.4 platform underwriting lifecycle controls.
public interface ILoanService
{
    // Generates a new loan record locked at the initial 'ApplicationSubmitted' state.
    Task<Loan> SubmitApplicationAsync(Guid borrowerId, long principalAmountKobo, int interestRateBps, int tenorDays, CancellationToken cancellationToken = default);

    // Forces a secure, domain-validated status transition for a specific active loan asset.
    Task<bool> UpdateLoanStatusAsync(Guid loanId, LoanStatus newStatus, CancellationToken cancellationToken = default);

    // Executes atomic financial disbursement for an approved loan asset.
    // Debits platform treasury pools, credits borrower wallets, and updates lifecycle timestamps.
    Task<bool> DisburseLoanAsync(Guid loanId, CancellationToken cancellationToken = default);
}