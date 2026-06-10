using PeerLend.Domain.Entities;

namespace PeerLend.Application.Common.Interfaces;

// Defines core operations for interacting with credit history registries.
// Enforces Section 8.1 algorithmic underwriting checks of the Engineering Bible.
public interface ICreditBureauService
{
    // Retrieves the consumer credit score and debt report summarized directly from the bureau registry.
    // Accepts the user entity and their validated identity token identifier string (BVN/NIN).
    Task<int> GetConsumerCreditScoreAsync(User user, string nationalIdentityToken, CancellationToken cancellationToken = default);
}
