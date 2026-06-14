namespace Demlo.Application.Common.Interfaces;

public interface IFinancialLedgerService
{
    // Atomically computes balance differentials and commits a permanent,
    // double-entry record to the transaction audit trail.
    Task<bool> LogTransactionAsync(
        string reference,
        string transactionType,
        Guid sourceAccountId,
        Guid destinationAccountId,
        long amountKobo,
        string? narrative,
        CancellationToken cancellationToken);
}