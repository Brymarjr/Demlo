using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Entities;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Infrastructure.Services;

public class FinancialLedgerService : IFinancialLedgerService
{
    private readonly DemloDbContext _context;

    public FinancialLedgerService(DemloDbContext context)
    {
        _context = context;
    }

    public async Task<bool> LogTransactionAsync(
        string reference,
        string transactionType,
        Guid sourceAccountId,
        Guid destinationAccountId,
        long amountKobo,
        string? narrative,
        CancellationToken cancellationToken)
    {
        if (amountKobo <= 0) return false;

        long sourceBefore = 0;
        long sourceAfter = 0;
        long destBefore = 0;
        long destAfter = 0;

        // 1. Snapshot Source Wallet state (Skip if Guid.Empty representing external gateway entry)
        if (sourceAccountId != Guid.Empty)
        {
            var sourceLender = await _context.LenderProfiles
                .FirstOrDefaultAsync(l => l.UserId == sourceAccountId, cancellationToken);
                
            if (sourceLender != null)
            {
                sourceBefore = sourceLender.AvailableBalanceKobo;
                sourceAfter = sourceBefore - amountKobo; // Value lost
            }
        }

        // 2. Snapshot Destination Wallet state (Skip if Guid.Empty representing external payout)
        if (destinationAccountId != Guid.Empty)
        {
            var destLender = await _context.LenderProfiles
                .FirstOrDefaultAsync(l => l.UserId == destinationAccountId, cancellationToken);
                
            if (destLender != null)
            {
                destBefore = destLender.AvailableBalanceKobo;
                destAfter = destBefore + amountKobo; // Value gained
            }
        }

        // 3. Assemble the permanent, immutable double-entry ledger logging node
        var ledgerEntry = new FinancialLedgerLog
        {
            Id = Guid.NewGuid(),
            TransactionReference = reference,
            TransactionType = transactionType,
            SourceAccountId = sourceAccountId,
            DestinationAccountId = destinationAccountId,
            AmountKobo = amountKobo,
            SourceBeforeBalanceKobo = sourceBefore,
            SourceAfterBalanceKobo = sourceAfter,
            DestinationBeforeBalanceKobo = destBefore,
            DestinationAfterBalanceKobo = destAfter,
            Narrative = narrative,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _context.FinancialLedgerLogs.AddAsync(ledgerEntry, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL LEDGER ERROR] Failed to append entry into audit trail: {ex.Message}");
            return false;
        }
    }
}