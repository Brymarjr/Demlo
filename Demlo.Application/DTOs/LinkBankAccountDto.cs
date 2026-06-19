using System.ComponentModel.DataAnnotations;

namespace Demlo.Application.DTOs;

public class LinkBankAccountDto
{
    [Required]
    public string AuthCode { get; set; } = string.Empty;
}