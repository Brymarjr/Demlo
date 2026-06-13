using System.ComponentModel.DataAnnotations;

namespace PeerLend.Domain.Entities;

// Tracks system-wide operation variables mutable only via authorized Admin entities.
public class GlobalPolicy
{
    [Key]
    public string Key { get; set; } = string.Empty; // e.g., "MIN_LOAN_PRINCIPAL_KOBO"

    [Required]
    public string Value { get; set; } = string.Empty; // stored as string for polymorphism

    [Required]
    public string Description { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}