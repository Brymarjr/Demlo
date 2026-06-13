using System.ComponentModel.DataAnnotations.Schema;
using Demlo.Domain.Common;

namespace Demlo.Domain.Entities;

// Represents an immutable singular transaction leg within the double-entry accounting ledger.
// Enforces Section 4.2 and Section 4.3 of the Engineering Bible.
public class LedgerEntry : BaseEntity
{
    // Foreign key identifier for the specific account being debited (funds reduced).
    public Guid DebitAccountId { get; set; }

    [ForeignKey(nameof(DebitAccountId))]
    public LedgerAccount DebitAccount { get; set; } = null!;

    // Foreign key identifier for the specific account being credited (funds increased).
    public Guid CreditAccountId { get; set; }

    [ForeignKey(nameof(CreditAccountId))]
    public LedgerAccount CreditAccount { get; set; } = null!;

    // The explicit amount of money moved during this transaction leg, stored strictly in Kobo.
    public long AmountKobo { get; set; }

    // Categorizes the transactional nature (e.g., Deposit, Disbursement, Repayment, PlatformFee).
    public string Type { get; set; } = string.Empty;

    // An external tracking ID linking this ledger transaction back to a specific domain entity like a Loan ID.
    public Guid ReferenceId { get; set; }
}
