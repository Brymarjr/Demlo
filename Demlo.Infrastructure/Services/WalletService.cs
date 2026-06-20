using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Infrastructure.Persistence;
using Demlo.Domain.Enums;
using Microsoft.Extensions.Configuration;

namespace Demlo.Infrastructure.Services;

// Implements transactional double-entry ledger book keeping protocols.
// Enforces Section 9.1 data integrity and absolute audit transparency.
public class WalletService : IWalletService
{
    private readonly DemloDbContext _context;
    private readonly IPaystackDisbursementService _paystackService; // ◄ NEW: Injected external gateway
    private readonly IConfiguration _configuration;

    public WalletService(DemloDbContext context, IPaystackDisbursementService paystackService, IConfiguration configuration)
    {
        _context = context;
        _paystackService = paystackService;
        _configuration = configuration;
    }

    public async Task<bool> ProvisionUserWalletAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var existingWallet = await _context.Wallets.AnyAsync(w => w.UserId == userId, cancellationToken);
            if (existingWallet) return true;

            var newWallet = new Domain.Entities.Wallet
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            await _context.Wallets.AddAsync(newWallet, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            Console.WriteLine($"[LEDGER SUCCESS] Pristine double-entry ledger wallet generated for User: {userId}");
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
        var wallet = await _context.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);
        if (wallet == null) return 0;

        var totalCredits = await _context.Transactions.AsNoTracking()
            .Where(t => t.WalletId == wallet.Id && t.Type == "CREDIT")
            .SumAsync(t => t.AmountKobo, cancellationToken);

        var totalDebits = await _context.Transactions.AsNoTracking()
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
        if (senderUserId == recipientUserId) throw new ArgumentException("Sender and recipient cannot be identical.");

        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var senderWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == senderUserId, cancellationToken);
            var recipientWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == recipientUserId, cancellationToken);

            if (senderWallet == null || recipientWallet == null) throw new InvalidOperationException("Associated wallets do not exist.");

            var senderBalance = await GetWalletBalanceAsync(senderUserId, cancellationToken);
            if (senderBalance < amountKobo) throw new InvalidOperationException("Transfer rejected due to insufficient available balance.");

            var debitRecord = new Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(), WalletId = senderWallet.Id, AmountKobo = amountKobo, Type = "DEBIT",
                Description = $"P2P Transfer to User {recipientUserId}: {description}", Timestamp = DateTime.UtcNow
            };

            var creditRecord = new Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(), WalletId = recipientWallet.Id, AmountKobo = amountKobo, Type = "CREDIT",
                Description = $"P2P Transfer from User {senderUserId}: {description}", Timestamp = DateTime.UtcNow
            };

            await _context.Transactions.AddRangeAsync(new[] { debitRecord, creditRecord }, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[CRITICAL LEDGER ABORT] Internal transfer crashed: {ex.Message}");
            return false;
        }
    }

    // Generate Paystack Deposit Checkout Link
    public async Task<string> RequestDepositLinkAsync(Guid userId, long amountKobo, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (user == null) throw new InvalidOperationException("User identity not found.");

        string email = string.IsNullOrWhiteSpace(user.Email) ? "funding@demlo.com" : user.Email;
        
        // We embed the UserId directly into the reference string (Format: dep_{UserId}_{Guid})
        string reference = $"dep_{userId:N}_{Guid.NewGuid():N}"; 

        return await _paystackService.InitializeDepositAsync(email, amountKobo, reference, cancellationToken);
    }

    // Execute External Wallet Cashout via Paystack Transfer
    public async Task<bool> RequestWithdrawalAsync(Guid userId, long amountKobo, CancellationToken cancellationToken = default)
    {
        // 1. Setup atomic transaction context
        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var user = await _context.Users.FindAsync(new object[] { userId }, cancellationToken);
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);

            if (user == null || wallet == null) throw new InvalidOperationException("Financial structures not found.");

            // 2. Validate liquidity
            var currentBalance = await GetWalletBalanceAsync(userId, cancellationToken);
            if (currentBalance < amountKobo) throw new InvalidOperationException("Insufficient clear balance for withdrawal.");

            // 3. Extract verified banking details based on the user's role architecture
            string accountNumber = string.Empty;
            string bankName = string.Empty;

            if (user.Role == UserRole.Borrower)
            {
                var profile = await _context.BorrowerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
                if (profile == null || string.IsNullOrEmpty(profile.BankAccountNumber)) throw new InvalidOperationException("Borrower has no linked bank account.");
                accountNumber = profile.BankAccountNumber;
                bankName = profile.BankName;
            }
            else if (user.Role == UserRole.Lender)
            {
                var profile = await _context.LenderProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
                if (profile == null || string.IsNullOrEmpty(profile.BankAccountNumber)) throw new InvalidOperationException("Lender has no linked bank account.");
                accountNumber = profile.BankAccountNumber;
                bankName = profile.BankName;
            }

            // 4. Resolve the CBN 3-digit bank code required by NIBSS/Paystack
            string bankCode = ResolveCbnBankCode(bankName);

            // 5. Instantly debit the user's internal Demlo wallet (to prevent double-spending during API transit)
            string withdrawalRef = $"wth_{Guid.NewGuid():N}";
            var debitRecord = new Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                AmountKobo = amountKobo,
                Type = "DEBIT",
                Description = $"External Bank Withdrawal to {bankName}",
                Timestamp = DateTime.UtcNow
            };

            await _context.Transactions.AddAsync(debitRecord, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            // 6. Fire the real-world HTTP transfer via Paystack
            var transferSuccess = await _paystackService.InitiateWalletWithdrawalAsync(amountKobo, bankCode, accountNumber, withdrawalRef, cancellationToken);

            if (!transferSuccess)
            {
                throw new InvalidOperationException("External bank network rejected the transfer execution.");
            }

            // 7. If everything succeeded, commit the database changes permanently
            await dbTransaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            // If Paystack fails, or validation fails, rollback the wallet debit instantly
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[WITHDRAWAL ABORT] {ex.Message}");
            return false;
        }
    }

    // Safely parses the reference, checks idempotency, and credits the wallet
    public async Task<bool> ProcessPaystackWebhookAsync(string reference, long amountKobo, CancellationToken cancellationToken = default)
    {
        // 1. Extract the UserId from the reference string
        var parts = reference.Split('_');
        if (parts.Length < 3 || parts[0] != "dep") return false;
        if (!Guid.TryParse(parts[1], out var userId)) return false;

        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, cancellationToken);
            if (wallet == null) return false;

            // 2. Idempotency Check: Prevent double-crediting if Paystack fires the webhook twice
            bool alreadyProcessed = await _context.Transactions.AnyAsync(t => t.Description.Contains(reference), cancellationToken);
            if (alreadyProcessed) 
            {
                Console.WriteLine($"[WEBHOOK IDEMPOTENT] Reference {reference} already settled. Skipping duplicate.");
                return true; 
            }

            // 3. Credit the Wallet
            var creditRecord = new Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                WalletId = wallet.Id,
                AmountKobo = amountKobo,
                Type = "CREDIT",
                Description = $"Paystack Wallet Funding - Ref: {reference}",
                Timestamp = DateTime.UtcNow
            };

            await _context.Transactions.AddAsync(creditRecord, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            Console.WriteLine($"[WEBHOOK SUCCESS] Wallet {wallet.Id} physically funded with {amountKobo} kobo via external gateway.");
            return true;
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[WEBHOOK FAULT] Ledger commit failed: {ex.Message}");
            return false;
        }
    }

    // Dynamically reads bank routings from appsettings.json without recompiling
    private string ResolveCbnBankCode(string bankName)
    {
        var standardizedName = bankName.ToLower().Trim();
        var bankCodes = _configuration.GetSection("BankCodes").GetChildren();

        foreach (var bank in bankCodes)
        {
            if (standardizedName.Contains(bank.Key))
            {
                return bank.Value!;
            }
        }

        throw new InvalidOperationException($"Bank routing code could not be dynamically resolved for '{bankName}'. Please contact support to map this institution.");
    }
}