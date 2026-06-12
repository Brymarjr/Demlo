using System.ComponentModel.DataAnnotations;

namespace PeerLend.Application.DTOs;

// Defines the inbound payload parameters required to submit a brand-new loan application.
public record LoanApplicationDto
{
    [Required]
    [Range(500000, 500000000, ErrorMessage = "Requested loan principal must be between 5,000 Naira (500,000 Kobo) and 5,000,000 Naira (500,000,000 Kobo).")]
    public long PrincipalAmountKobo { get; init; }

    [Required]
    [Range(100, 5000, ErrorMessage = "Interest rate must be specified between 100 Basis Points (1%) and 5000 Basis Points (50%).")]
    public int InterestRateBps { get; init; }

    [Required]
    [Range(7, 365, ErrorMessage = "Loan duration tenor must span between 7 days and 365 days.")]
    public int TenorDays { get; init; }
}