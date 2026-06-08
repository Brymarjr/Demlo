using System.ComponentModel.DataAnnotations.Schema;
using PeerLend.Domain.Common;

namespace PeerLend.Domain.Entities;

// Tracks total capital metrics, liquidity, and aggregated yield positions for lenders.
// Enforces Section 4.2 of the Engineering Bible schema design.
public class LenderProfile : BaseEntity
{
    // The foreign key linking this specific portfolio directly back to the master User account.
    public Guid UserId { get; set; }

    // Navigation property for Entity Framework Core to handle relational joins back to the User.
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    // Cumulative sum of all real-money physical cash deposits initiated by the lender.
    // Stored strictly in Kobo.
    public long TotalDepositedKobo { get; set; }

    // Liquid cash capital currently sitting in the escrow pool that has not yet been algorithmically matched.
    // Available for instant payout withdrawal requests. Stored strictly in Kobo.
    public long AvailableBalanceKobo { get; set; }

    // Cumulative historical sum of all interest distributions earned from matched fractional loans.
    // Stored strictly in Kobo.
    public long TotalEarnedKobo { get; set; }
}
