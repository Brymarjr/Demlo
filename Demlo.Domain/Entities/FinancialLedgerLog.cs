using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Demlo.Domain.Common;

namespace Demlo.Domain.Entities;

// Represents the immutable, double-entry financial ledger audit trail.
// Enforces absolute accountability for corporate banking audits.
public class FinancialLedgerLog : BaseEntity
{
    // The unique string tracking token used for reconciliation (e.g., paystack ref, mono ref, internal match token)
    [Required]
    [MaxLength(100)]
    public string TransactionReference { get; set; } = null!;

    // Dictates the structural event type: "WALLET_FUNDING", "LOAN_DISBURSEMENT", "REPAYMENT_LIQUIDATION", "FEE_ASSESSMENT"
    [Required]
    [MaxLength(50)]
    public string TransactionType { get; set; } = null!;

    // The account losing funds. If Guid.Empty, this represents an influx from an external banking gateway
    public Guid SourceAccountId { get; set; }

    // The account gaining funds. If Guid.Empty, this represents an outward cash payout to a bank network
    public Guid DestinationAccountId { get; set; }

    // The exact gross value of the financial movement stored strictly in Kobo
    public long AmountKobo { get; set; }

    // Audit State Tracking: Captured to allow instant chronological ledger reconstruction
    public long SourceBeforeBalanceKobo { get; set; }
    public long SourceAfterBalanceKobo { get; set; }
    public long DestinationBeforeBalanceKobo { get; set; }
    public long DestinationAfterBalanceKobo { get; set; }

    // Optional contextual details to make the audit row easily readable by humans
    [MaxLength(500)]
    public string? Narrative { get; set; }
}