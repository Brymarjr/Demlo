using System.ComponentModel.DataAnnotations;

namespace Demlo.Application.DTOs;

public class RetryKycDto
{
    [Required]
    public string IdType { get; set; } = string.Empty; // e.g., "BVN" or "NIN"

    [Required]
    [StringLength(11, MinimumLength = 11, ErrorMessage = "ID Number must be exactly 11 digits.")]
    public string IdNumber { get; set; } = string.Empty;
}