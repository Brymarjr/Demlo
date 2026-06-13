using Microsoft.EntityFrameworkCore;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Domain.Entities;
using PeerLend.Infrastructure.Persistence;

namespace PeerLend.Infrastructure.Services;

// Implements real-time policy modifications and corporate security auditing trackers.
// Enforces Section 13.2 administrative governance guidelines of the Engineering Bible.
public class GlobalPolicyEngine : IGlobalPolicyEngine
{
    private readonly PeerLendDbContext _context;

    public GlobalPolicyEngine(PeerLendDbContext context)
    {
        _context = context;
    }

    public async Task<string> GetPolicyValueAsync(string key, string defaultValue, CancellationToken cancellationToken = default)
    {
        var policy = await _context.GlobalPolicies
            .AsNoTracking() // Read-only query performance tuning optimization
            .FirstOrDefaultAsync(p => p.Key == key, cancellationToken);

        return policy == null ? defaultValue : policy.Value;
    }

    public async Task<bool> UpdatePolicyAsync(string key, string newValue, string adminActor, CancellationToken cancellationToken = default)
    {
        // 1. Initialize an atomic transaction window to guarantee that a log failure rolls back the update
        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var policy = await _context.GlobalPolicies.FirstOrDefaultAsync(p => p.Key == key, cancellationToken);
            string oldValue = "NON_EXISTENT";

            if (policy == null)
            {
                // Create the metric dynamically if it has not been seeded yet
                policy = new GlobalPolicy
                {
                    Key = key,
                    Value = newValue,
                    Description = $"System-generated structural default parameter key override."
                };
                await _context.GlobalPolicies.AddAsync(policy, cancellationToken);
            }
            else
            {
                oldValue = policy.Value;
                policy.Value = newValue;
                policy.UpdatedAt = DateTime.UtcNow;
            }

            // 2. Generate the balancing, immutable Security Audit Trail footprint row
            var auditTrailRecord = new AuditLog
            {
                Id = Guid.NewGuid(),
                Actor = adminActor,
                ActionType = "POLICY_ALTERATION",
                Details = $"{{\"key\":\"{key}\",\"old_value\":\"{oldValue}\",\"new_value\":\"{newValue}\",\"machine\":\"{Environment.MachineName}\"}}",
                Timestamp = DateTime.UtcNow
            };

            await _context.AuditLogs.AddAsync(auditTrailRecord, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            await dbTransaction.CommitAsync(cancellationToken);
            Console.WriteLine($"[GOVERNANCE SUCCESS] Admin '{adminActor}' shifted policy parameter '{key}' from [{oldValue}] ──► [{newValue}]");
            return true;
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[GOVERNANCE CRITICAL ABORT] Failed to mutate global policy metrics safely: {ex.Message}");
            return false;
        }
    }
}