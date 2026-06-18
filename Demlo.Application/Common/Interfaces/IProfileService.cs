using Demlo.Application.DTOs;

namespace Demlo.Application.Common.Interfaces;

public interface IProfileService
{
    Task<bool> UpdateBorrowerProfileAsync(Guid userId, UpdateBorrowerProfileDto request, CancellationToken cancellationToken = default);

    Task<bool> UpdateLenderProfileAsync(Guid userId, UpdateLenderProfileDto request, CancellationToken cancellationToken = default);
}