using PeerLend.Domain.Entities;

namespace PeerLend.Application.Common.Interfaces;

// Defines core operations for internal financial wallets and ledger management.
// Enforces Section 9.1 mathematical balance standards of the Engineering Bible.
public interface IWalletService
{
    // Provisions a pristine, zero-balance virtual wallet and double-entry ledger account for a verified user.
    Task<bool> ProvisionUserWalletAsync(Guid userId, CancellationToken cancellationToken = default);

    // Retrieves the current real-time clear balance for a specific user's wallet engine.
    Task<long> GetWalletBalanceAsync(Guid userId, CancellationToken cancellationToken = default);
}
