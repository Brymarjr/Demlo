using PeerLend.Application.DTOs;

namespace PeerLend.Application.Common.Interfaces;

// Defines user account orchestration use cases mandated by Section 6 of the Engineering Bible.
public interface IUserService
{
    // Handles secure account creation, identity anonymization, and automatic profile generation.
    Task<Guid> RegisterUserAsync(RegisterUserDto request, CancellationToken cancellationToken = default);

    // Validates a user's submitted OTP against the Redis cache. Returns true on success.
    Task<bool> VerifyOtpAsync(VerifyOtpDto request, CancellationToken cancellationToken = default);
}
