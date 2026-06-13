using System.ComponentModel.DataAnnotations;

namespace Demlo.Application.DTOs;

// Defines the inbound payload constraints for peer-to-peer wallet transfer requests.
public record WalletTransferDto
{
    [Required]
    public Guid RecipientUserId { get; init; }

    [Required]
    [Range(100, long.MaxValue, ErrorMessage = "Minimum transfer threshold is 100 kobo (1 Naira).")]
    public long AmountKobo { get; init; }

    [MaxLength(200, ErrorMessage = "Transaction description text details cannot exceed 200 characters.")]
    public string Description { get; init; } = "Demlo Internal Transfer";
}