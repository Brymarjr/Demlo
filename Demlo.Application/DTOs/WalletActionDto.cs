using System.ComponentModel.DataAnnotations;

namespace Demlo.Application.DTOs;

public class DepositRequestDto
{
    [Required]
    [Range(10000, 1000000000, ErrorMessage = "Deposit must be between ₦100 and ₦10,000,000")]
    public long AmountKobo { get; set; }
}

public class WithdrawRequestDto
{
    [Required]
    [Range(10000, 1000000000, ErrorMessage = "Withdrawal must be between ₦100 and ₦10,000,000")]
    public long AmountKobo { get; set; }
}