using Demlo.Domain.Entities;

namespace Demlo.Application.Common.Interfaces;

// Defines core operations for internal financial wallets and ledger management.
// Enforces Section 9.1 mathematical balance standards of the Engineering Bible.
public interface IWalletService
{
    // Provisions a pristine, zero-balance virtual wallet and double-entry ledger account for a verified user.
    Task<bool> ProvisionUserWalletAsync(Guid userId, CancellationToken cancellationToken = default);

    // Retrieves the current real-time clear balance for a specific user's wallet engine.
    Task<long> GetWalletBalanceAsync(Guid userId, CancellationToken cancellationToken = default);

    // Executes a real-time ledger deposit or withdrawal for an internal wallet profile.
    // Type parameters must strictly validate to "CREDIT" or "DEBIT".
    Task<bool> ProcessTransactionAsync(Guid walletId, long amountKobo, string type, string description, CancellationToken cancellationToken = default);

    // Executes a secure internal peer-to-peer wallet-to-wallet transfer between two users.
    // Deducts from the sender and adds to the recipient under an atomic database transaction.
    Task<bool> TransferFundsAsync(Guid senderUserId, Guid recipientUserId, long amountKobo, string description, CancellationToken cancellationToken = default);

    // Generates a funding link for the frontend
    Task<string> RequestDepositLinkAsync(Guid userId, long amountKobo, CancellationToken cancellationToken = default);

    // Orchestrates a wallet debit and external bank payout
    Task<bool> RequestWithdrawalAsync(Guid userId, long amountKobo, CancellationToken cancellationToken = default);

    // Processes the inbound webhook from Paystack securely
    Task<bool> ProcessPaystackWebhookAsync(string reference, long amountKobo, CancellationToken cancellationToken = default);
}
