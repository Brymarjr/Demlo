using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Infrastructure.Services;

// Implements transactional double-entry ledger book keeping protocols.
// Enforces Section 9.1 data integrity and absolute audit transparency.
public class WalletService : IWalletService
{
    private readonly DemloDbContext _context;

    public WalletService(DemloDbContext context)
    {
        _context = context;
    }

    public async Task<bool> ProvisionUserWalletAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Verify if an internal account entry structure already exists for this identity
            var existingWallet = await _context.Wallets.AnyAsync(w => w.UserId == userId, cancellationToken);
            if (existingWallet)
            {
                Console.WriteLine($"[LEDGER INFO] Wallet structure is already provisioned for individual user profile context: {userId}");
                return true;
            }

            // 2. Initialize a secure domain container mapping record inside our relational mapping tree
            // Assuming your domain model context uses standard 'Wallet' mapping variables linked to Core Users
            var newWallet = new Domain.Entities.Wallet
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _context.Wallets.AddAsync(newWallet, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            Console.WriteLine($"[LEDGER SUCCESS] Pristine transaction-safe double-entry ledger wallet generated for User: {userId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL LEDGER FAULT] Failed to allocate user financial tracking structures: {ex.Message}");
            return false;
        }
    }

    public async Task<long> GetWalletBalanceAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // 1. Find the target wallet reference token id
        var wallet = await _context.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

        if (wallet == null)
        {
            return 0; // Fallback to zero state if a lookup execution occurs prematurely
        }

        // 2. Compute the current user clear cash balance dynamically out of the transaction ledger.
        // Summing Credits minus Debits guarantees total tracking balance accountability (Section 9.1)
        // If your database schema stores explicit TransactionLedger rows, we calculate them directly:
        var totalCredits = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.WalletId == wallet.Id && t.Type == "CREDIT")
            .SumAsync(t => t.AmountKobo, cancellationToken);

        var totalDebits = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.WalletId == wallet.Id && t.Type == "DEBIT")
            .SumAsync(t => t.AmountKobo, cancellationToken);

        return totalCredits - totalDebits;
    }

    public async Task<bool> ProcessTransactionAsync(Guid walletId, long amountKobo, string type, string description, CancellationToken cancellationToken = default)
    {
        if (amountKobo <= 0) throw new ArgumentException("Transaction amount must be greater than zero kobo.");
        if (type != "CREDIT" && type != "DEBIT") throw new ArgumentException("Invalid ledger transaction type classification.");

        var transactionEntry = new Domain.Entities.Transaction
        {
            Id = Guid.NewGuid(),
            WalletId = walletId,
            AmountKobo = amountKobo,
            Type = type,
            Description = description,
            Timestamp = DateTime.UtcNow
        };

        await _context.Transactions.AddAsync(transactionEntry, cancellationToken);
        var affectedRows = await _context.SaveChangesAsync(cancellationToken);

        return affectedRows > 0;
    }

    public async Task<bool> TransferFundsAsync(Guid senderUserId, Guid recipientUserId, long amountKobo, string description, CancellationToken cancellationToken = default)
    {
        if (amountKobo <= 0) throw new ArgumentException("Transfer amount must be greater than zero kobo.");
        if (senderUserId == recipientUserId) throw new ArgumentException("Sender and recipient configurations cannot be identical.");

        // Initialize an atomic database execution transaction strategy context
        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // 1. Fetch and confirm both target wallet maps exist
            var senderWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == senderUserId, cancellationToken);
            var recipientWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == recipientUserId, cancellationToken);

            if (senderWallet == null || recipientWallet == null)
            {
                throw new InvalidOperationException("One or both associated financial routing wallets do not exist.");
            }

            // 2. Enforce liquidity rules: Verify the sender actually has enough clear balance
            var senderBalance = await GetWalletBalanceAsync(senderUserId, cancellationToken);
            if (senderBalance < amountKobo)
            {
                throw new InvalidOperationException("Transfer execution rejected due to insufficient available ledger liquidity balance.");
            }

            // 3. Post the balancing double-entry ledger records
            // Debit the sender's channel (Money moves OUT)
            var debitRecord = new Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                WalletId = senderWallet.Id,
                AmountKobo = amountKobo,
                Type = "DEBIT",
                Description = $"P2P Transfer to User {recipientUserId}: {description}",
                Timestamp = DateTime.UtcNow
            };

            // Credit the recipient's channel (Money moves IN)
            var creditRecord = new Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                WalletId = recipientWallet.Id,
                AmountKobo = amountKobo,
                Type = "CREDIT",
                Description = $"P2P Transfer from User {senderUserId}: {description}",
                Timestamp = DateTime.UtcNow
            };

            await _context.Transactions.AddRangeAsync(new[] { debitRecord, creditRecord }, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            // Commit the transaction to disk atomically
            await dbTransaction.CommitAsync(cancellationToken);
            Console.WriteLine($"[LEDGER SUCCESS] Atomic P2P Transfer finalized cleanly. Amount: {amountKobo} kobo. Sender: {senderUserId} -> Recipient: {recipientUserId}");
            return true;
        }
        catch (Exception ex)
        {
            // Roll back all changes to their pristine state if any part of the execution breaks
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[CRITICAL LEDGER ABORT] Internal transfer pipeline crashed. Full rollback executed. Reason: {ex.Message}");
            return false;
        }
    }
}
