namespace Demlo.Domain.Enums;

/// Represents the definitive, mutually exclusive states of a loan lifecycle.
/// Enforces Section 4.4 of the Engineering Bible.

public enum LoanStatus
{
    ApplicationSubmitted = 1,
    KycPending = 2,
    CreditScoring = 3,
    Approved = 4,
    AwaitingMatch = 5,
    Matched = 6,
    Disbursed = 7,
    Active = 8,
    Overdue = 9,
    Defaulted = 10,
    ClosedRepaid = 11,
    ClosedWrittenOff = 12,
    Rejected = 13
}
