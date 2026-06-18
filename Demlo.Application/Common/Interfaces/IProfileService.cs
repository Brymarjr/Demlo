using Demlo.Application.DTOs;

namespace Demlo.Application.Common.Interfaces;

public interface IProfileService
{
    Task<bool> UpdateBorrowerProfileAsync(Guid userId, UpdateBorrowerProfileDto request, CancellationToken cancellationToken = default);
}