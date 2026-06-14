using System.ComponentModel.DataAnnotations;
using Demlo.Domain.Common;

namespace Demlo.Domain.Entities;

public class LenderRiskFundPool : BaseEntity
{
    // A distinct code identifier referencing the global pool (e.g., "LRF_MASTER_NGN")
    [Required]
    [MaxLength(50)]
    public string PoolCode { get; set; } = "LRF_MASTER_NGN";

    // The authoritative real-time insurance capital reserves balance tracked strictly in Kobo
    public long TotalReservesKobo { get; set; }

    // Last time the pool balance was updated by a fee collector or a liquidation claim
    public DateTime LastUpdatedAt { get; set; }
}