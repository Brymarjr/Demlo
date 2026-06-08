using System.ComponentModel.DataAnnotations.Schema;
using PeerLend.Domain.Common;

namespace PeerLend.Domain.Entities;

// Tracks individual fractional lender capital assignments and earnings per specific loan.
// Enforces Section 4.2 of the Engineering Bible schema design.
public class LoanAllocation : BaseEntity
{
    // The foreign key tying this specific financial fragment back to the master Loan asset.
    public Guid LoanId { get; set; }

    // Navigation property for Entity Framework Core to handle relational joins to the Loan.
    [ForeignKey(nameof(LoanId))]
    public Loan Loan { get; set; } = null!;

    // The foreign key tying this specific asset assignment back to the funding Lender profile.
    public Guid LenderId { get; set; }

    // Navigation property for Entity Framework Core to handle relational joins to the Lender's profile.
    [ForeignKey(nameof(LenderId))]
    public LenderProfile Lender { get; set; } = null!;

    // The explicit portion of capital deployed by this specific lender for this loan unit fraction.
    // Stored strictly in Kobo (e.g., standard baseline unit of 500,000 Kobo / NGN 5,000).
    public long AllocatedAmountKobo { get; set; }

    // The cumulative interest metrics returned and credited back to this lender from borrower repayments.
    // Stored strictly in Kobo.
    public long InterestEarnedKobo { get; set; }

    // Tracks if this specific allocation fragment is actively yielding or impacted by default.
    public string Status { get; set; } = string.Empty;
}
