using System.ComponentModel.DataAnnotations;
using Demlo.Domain.Common;

namespace Demlo.Domain.Entities;

public class LedgerReconciliationAudit : BaseEntity
{
    public DateTime AuditDate { get; set; }
    
    public long TotalDebitsKobo { get; set; }
    
    public long TotalCreditsKobo { get; set; }
    
    // The net variance (TotalCreditsKobo - TotalDebitsKobo). Must be 0 for a balanced ledger.
    public long VarianceKobo { get; set; }
    
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "BALANCED"; // BALANCED or UNBALANCED_WARN
    
    // Holds the complete, raw comma-separated value string payload for direct downloading
    [Required]
    public string CsvPayload { get; set; } = string.Empty;
}