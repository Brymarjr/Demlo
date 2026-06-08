using System.ComponentModel.DataAnnotations.Schema;
using PeerLend.Domain.Common;

namespace PeerLend.Domain.Entities;

// Stores extended financial and credit underwriting parameters specific to borrowers.
// Enforces Section 4.2 of the Engineering Bible schema design.
public class BorrowerProfile : BaseEntity
{
    // The foreign key linking this specific profile directly back to the master User account.
    public Guid UserId { get; set; }

    // Navigation property for Entity Framework Core to handle relational joins back to the User.
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    // The composite numeric credit rating calculated by the Layer 4 rules engine.
    // Scales on a defined continuum from 300 to 850.
    public int CreditScore { get; set; }

    // The maximum aggregate loan balance this borrower can actively draw down at one time.
    // Stored strictly in Kobo (BIGINT equivalent) to guarantee zero precision loss.
    public long MaxLoanLimitKobo { get; set; }

    // The verified monthly net inflow proxy calculated programmatically from open banking data.
    // Stored strictly in Kobo.
    public long IncomeKobo { get; set; }

    // The unique identifier referencing the borrower's primary linked bank account inside Mono.
    // Used to look up statement histories and trigger recurring direct debit pulls.
    public string BankAccountId { get; set; } = string.Empty;
}
