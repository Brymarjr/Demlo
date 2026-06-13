using System.ComponentModel.DataAnnotations;

namespace PeerLend.Domain.Entities;

// An immutable registry tracking critical corporate actions and systemic modifications.
public class AuditLog
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string Actor { get; set; } = string.Empty; // Admin email or system user token

    [Required]
    public string ActionType { get; set; } = string.Empty; // e.g., "POLICY_ALTERATION"

    [Required]
    public string Details { get; set; } = string.Empty; // JSON or descriptive audit trail payload

    [Required]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}