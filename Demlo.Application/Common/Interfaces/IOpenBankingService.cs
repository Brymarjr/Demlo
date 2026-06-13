using Demlo.Domain.Entities;

namespace Demlo.Application.Common.Interfaces;

// Defines core operations for retail open banking data synchronization.
// Enforces Section 7.1 financial ingestion rules of the Engineering Bible.
public interface IOpenBankingService
{
    // Exchanges a public single-use widget authentication token code for a permanent Mono Account ID string.
    Task<string> ExchangeAuthCodeForAccountIdAsync(string publicAuthCode, CancellationToken cancellationToken = default);

    // Synchronizes and updates live banking metadata and ledger history for a specific user.
    Task<bool> SynchronizeAccountTelemetryAsync(User user, string accountId, CancellationToken cancellationToken = default);
}