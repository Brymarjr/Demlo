using Demlo.Domain.Entities;

namespace Demlo.Application.Common.Interfaces;

// Defines core operations for statutory identity verification and compliance checks.
// Enforces Section 6.3 KYC pipeline constraints of the Engineering Bible.
public interface IKycService
{
    // Submits a background verification request for BVN validation.
    Task<bool> VerifyBvnAsync(User user, string rawBvn, CancellationToken cancellationToken = default);

    // Submits a background verification request for NIN validation.
    Task<bool> VerifyNinAsync(User user, string rawNin, CancellationToken cancellationToken = default);

    Task<bool> SubmitKycAsync(Guid userId, CancellationToken cancellationToken = default);
}
