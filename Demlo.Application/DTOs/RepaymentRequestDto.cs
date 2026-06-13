using System.ComponentModel.DataAnnotations;

namespace Demlo.Application.DTOs;

// Defines the inbound payload parameters required to submit a loan repayment action.
public record RepaymentRequestDto
{
    [Required]
    public Guid LoanId { get; init; }

    [Required]
    [Range(100, long.MaxValue, ErrorMessage = "Minimum processing payment threshold is 100 kobo (1 Naira).")]
    public long AmountKobo { get; init; }
}