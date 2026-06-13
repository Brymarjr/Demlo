namespace PeerLend.Application.Common.Interfaces;

public interface IGlobalPolicyEngine
{
    // Fetches configuration values dynamically. Returns defaultValue if configuration key doesn't exist.
    Task<string> GetPolicyValueAsync(string key, string defaultValue, CancellationToken cancellationToken = default);

    // Updates a specific configuration metric and drops a matching immutable row into the corporate Audit Log tracker.
    Task<bool> UpdatePolicyAsync(string key, string newValue, string adminActor, CancellationToken cancellationToken = default);
}