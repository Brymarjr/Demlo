namespace Demlo.Application.Common.Interfaces;

public interface ILedgerLiquidationService
{
    // Idempotently distributes a incoming borrower repayment across fractional lender allocations.
    // Enforces strict double-entry balancing rules specified in Section 4.3 of the Bible.
    Task<bool> DistributeRepaymentAsync(Guid loanId, long totalReceivedKobo, CancellationToken cancellationToken);
}