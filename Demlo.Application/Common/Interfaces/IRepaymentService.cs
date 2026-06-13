using Demlo.Domain.Entities;

namespace Demlo.Application.Common.Interfaces;

// Outlines core use cases for tracking customer loan settlements and repayments.
// Enforces Section 11.1 mathematical balance standards of the Engineering Bible.
public interface IRepaymentService
{
    // Processes a repayment amount against an active loan asset.
    // Deducts funds from the user's wallet via a double-entry debit record.
    Task<bool> ProcessRepaymentAsync(Guid loanId, long amountKobo, CancellationToken cancellationToken = default);

    // Computes the absolute remaining total kobo value required to completely settle a loan asset.
    Task<long> CalculateOutstandingBalanceAsync(Guid loanId, CancellationToken cancellationToken = default);
}