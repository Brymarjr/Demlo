using PeerLend.Domain.Common;

namespace PeerLend.Domain.Entities;

// Represents an authoritative virtual financial account balance within our core architecture.
// Enforces Section 4.2 and Section 4.3 of the Engineering Bible.
public class LedgerAccount : BaseEntity
{
    // The identifier linking this ledger account to its owner. 
    // This maps directly to a User ID or a dedicated System Account Identifier.
    public Guid OwnerId { get; set; }

    // Identifies the account classification (e.g., Escrow, Reserve, Fee, Outstanding, or User).
    public string AccountType { get; set; } = string.Empty;

    // The current net running balance of the account, updated exclusively via entry balancing.
    // Stored strictly in Kobo (BIGINT equivalent) to guarantee absolute mathematical precision.
    public long BalanceKobo { get; set; }
}
