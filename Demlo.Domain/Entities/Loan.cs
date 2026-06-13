using System.ComponentModel.DataAnnotations.Schema;
using Demlo.Domain.Common;
using Demlo.Domain.Enums;

namespace Demlo.Domain.Entities;

// Represents the core loan asset and dictates the system state machine.
// Enforces Section 4.2 and Section 4.4 of the Engineering Bible.
public class Loan : BaseEntity
{
    // The foreign key linking this specific loan record directly back to the Borrower.
    public Guid BorrowerId { get; set; }

    // Navigation property for Entity Framework Core to handle relational joins to the Borrower's profile.
    [ForeignKey(nameof(BorrowerId))]
    public BorrowerProfile Borrower { get; set; } = null!;

    // The core principal amount borrowed, stored strictly in Kobo to ensure zero decimal drift.
    public long PrincipalAmountKobo { get; set; }

    // The annualized interest rate expressed in Basis Points (BPS).
    // For example, an 18% per annum rate is stored as 1800 basis points.
    public int InterestRateBps { get; set; }

    // The current, runtime state of the loan lifecycle.
    public LoanStatus Status { get; set; } = LoanStatus.ApplicationSubmitted;

    // The absolute lifecycle length of the loan agreement contract.
    public int TenorDays { get; set; }

    // The exact timestamp when the Paystack bank transfer successfully settled into the borrower's account.
    public DateTime? DisbursedAt { get; set; }

    // The absolute deadline date by which the full loan obligations must be completely paid back.
    public DateTime? DueAt { get; set; }

    // Method to enforce valid state machine transitions at the domain boundary.
    // Prevents illegal status jumps across our system architecture.
    public void TransitionTo(LoanStatus newStatus)
    {
        bool isValid = Status switch
        {
            LoanStatus.ApplicationSubmitted => newStatus == LoanStatus.KycPending || newStatus == LoanStatus.Rejected,
            LoanStatus.KycPending => newStatus == LoanStatus.CreditScoring || newStatus == LoanStatus.Rejected,
            LoanStatus.CreditScoring => newStatus == LoanStatus.Approved || newStatus == LoanStatus.Rejected,
            LoanStatus.Approved => newStatus == LoanStatus.AwaitingMatch,
            LoanStatus.AwaitingMatch => newStatus == LoanStatus.Matched,
            LoanStatus.Matched => newStatus == LoanStatus.Disbursed || newStatus == LoanStatus.AwaitingMatch,
            LoanStatus.Disbursed => newStatus == LoanStatus.Active,
            LoanStatus.Active => newStatus == LoanStatus.Overdue || newStatus == LoanStatus.ClosedRepaid,
            LoanStatus.Overdue => newStatus == LoanStatus.Active || newStatus == LoanStatus.Defaulted,
            LoanStatus.Defaulted => newStatus == LoanStatus.ClosedWrittenOff,

            // Terminal states cannot transition into any other status
            LoanStatus.ClosedRepaid => false,
            LoanStatus.ClosedWrittenOff => false,
            LoanStatus.Rejected => false,
            _ => false
        };

        if (!isValid)
        {
            throw new InvalidOperationException($"Invalid loan state transition from {Status} to {newStatus}.");
        }

        Status = newStatus;
    }
}
