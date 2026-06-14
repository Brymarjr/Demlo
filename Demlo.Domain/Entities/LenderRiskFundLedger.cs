using System.ComponentModel.DataAnnotations;
using Demlo.Domain.Common;

namespace Demlo.Domain.Entities;

public class LenderRiskFundLedger : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string TransactionReference { get; set; } = null!;

    // Dictates the transactional flow type: "PREMIUM_INFLUX" or "DEFAULT_CLAIM_OUTFLUX"
    [Required]
    [MaxLength(30)]
    public string EntryType { get; set; } = null!;

    public Guid AssociatedLoanId { get; set; }

    public long AmountKobo { get; set; }

    // Snapshots of the pool balance captured chronologically for financial audit validation
    public long BalanceBeforeKobo { get; set; }
    public long BalanceAfterKobo { get; set; }

    [MaxLength(250)]
    public string? Narrative { get; set; }
}