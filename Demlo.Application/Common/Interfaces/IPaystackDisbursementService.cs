namespace Demlo.Application.Common.Interfaces;

public interface IPaystackDisbursementService
{
    // ──► PHASE 8: Your existing loan disbursement pipeline
    Task<bool> InitiateLoanDisbursementAsync(Guid loanId, CancellationToken cancellationToken);

    // ──► PHASE 7 (NEW): Generates a secure Paystack Checkout URL for wallet funding
    Task<string> InitializeDepositAsync(string email, long amountKobo, string reference, CancellationToken cancellationToken = default);

    // ──► PHASE 7 (NEW): Executes an external NIBSS bank transfer to cash out a user's wallet
    Task<bool> InitiateWalletWithdrawalAsync(long amountKobo, string bankCode, string accountNumber, string reference, CancellationToken cancellationToken = default);
}