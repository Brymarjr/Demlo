namespace Demlo.Application.DTOs;

public class ForgotPasswordDto
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ResetPasswordDto
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Otp { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordDto
{
    public string OldPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}