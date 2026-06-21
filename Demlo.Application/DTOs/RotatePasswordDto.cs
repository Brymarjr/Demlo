namespace Demlo.Application.DTOs;

public class RotatePasswordDto
{
    public Guid UserId { get; set; }
    public string TemporaryPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}