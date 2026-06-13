using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Enums;
using Demlo.Domain.Entities;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Workers.Jobs;

// An idempotent financial audit routine designed to run inside the Hangfire server container.
// Enforces Section 13.1 automated overdue mutations and late fee policy application.
public class LoanDelinquencyJob
{
    private readonly DemloDbContext _context;
    private readonly IGlobalPolicyEngine _policyEngine;
    private readonly ILoanService _loanService;

    // Hangfire fully supports standard constructor Dependency Injection out-of-the-box
    public LoanDelinquencyJob(
        DemloDbContext context,
        IGlobalPolicyEngine policyEngine,
        ILoanService loanService)
    {
        _context = context;
        _policyEngine = policyEngine;
        _loanService = loanService;
    }

    // Executed nightly via Hangfire's CRON scheduler scheduler mechanism
    public async Task RunDailyAuditAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[HANGFIRE CRON] Commencing daily delinquency audit check at: {DateTime.UtcNow}");

        // 1. Fetch our late-fee penalty criteria with a safe fallback default (100 Kobo/1 Naira per day)
        string penaltyPolicyValue = await _policyEngine.GetPolicyValueAsync("DAILY_LATE_FEE_KOBO", "100", cancellationToken);
        long dailyLateFeeKobo = long.Parse(penaltyPolicyValue);

        // 2. Query for all Disbursed loans that have passed their maturity deadline
        var delinquentLoans = await _context.Loans
            .Where(l => l.Status == LoanStatus.Disbursed && DateTime.UtcNow > l.DueAt)
            .ToListAsync(cancellationToken);

        foreach (var loan in delinquentLoans)
        {
            // 3. Mutate the loan domain state safely to Overdue
            Console.WriteLine($"[DELINQUENCY DETECTED] Asset {loan.Id} has expired. Enforcing state transition via Hangfire.");
            await _loanService.UpdateLoanStatusAsync(loan.Id, LoanStatus.Overdue, cancellationToken);

            var borrowerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == loan.BorrowerId, cancellationToken);
            if (borrowerWallet == null) continue;

            // 4. Generate a balancing double-entry DEBIT ledger transaction line to record the fee liability
            var penaltyDebitRecord = new Transaction
            {
                Id = Guid.NewGuid(),
                WalletId = borrowerWallet.Id,
                AmountKobo = dailyLateFeeKobo,
                Type = "DEBIT",
                Description = $"Daily Late Fee Penalty Overdue Accumulation - Asset Reference ID: {loan.Id}",
                Timestamp = DateTime.UtcNow
            };

            await _context.Transactions.AddAsync(penaltyDebitRecord, cancellationToken);
        }

        if (delinquentLoans.Any())
        {
            await _context.SaveChangesAsync(cancellationToken);
            Console.WriteLine($"[HANGFIRE SUCCESS] Successfully penalized {delinquentLoans.Count} delinquent assets.");
        }
    }
}