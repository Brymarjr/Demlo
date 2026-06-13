namespace Demlo.Application.Common.Interfaces;

// Defines core outbound messaging operations.
// Enforces Section 6.2 statutory verification steps of the Engineering Bible.
public interface ISmsService
{
    // Transmits a verification OTP token to a destination Nigerian phone number.
    Task<bool> SendVerificationOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken = default);
}