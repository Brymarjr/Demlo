namespace Demlo.Application.Common.Interfaces;

public interface IPaystackDisbursementService
{
    // Programmatically generates a Paystack Transfer Recipient and initiates an external 
    // bank transfer across the NIBSS network using strict idempotency tracking keys.
    Task<bool> InitiateLoanDisbursementAsync(Guid loanId, CancellationToken cancellationToken);
}