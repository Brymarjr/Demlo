using System.ComponentModel.DataAnnotations;

namespace PeerLend.Domain.Entities;

// An immutable registry tracking critical corporate actions and systemic modifications.
// Enforces Section 4.2 data schema constraints from the Engineering Bible.
public class AuditLog
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid ActorId { get; set; }

    [Required]
    public string EntityType { get; set; } = string.Empty;

    [Required]
    public Guid EntityId { get; set; }

    [Required]
    public string Action { get; set; } = string.Empty;

    [Required]
    public string OldState { get; set; } = string.Empty;

    [Required]
    public string NewState { get; set; } = string.Empty;

    [Required]
    public string Ip { get; set; } = string.Empty;

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}