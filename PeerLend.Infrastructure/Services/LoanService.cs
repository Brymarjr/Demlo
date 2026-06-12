using Microsoft.EntityFrameworkCore;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Domain.Entities;
using PeerLend.Domain.Enums;
using PeerLend.Infrastructure.Persistence;

namespace PeerLend.Infrastructure.Services;

// Implements the centralized state machine runner and lifecycle processor for active loans.
// Enforces Section 4.2 domain transition policies and injects real-time milestone alerts.
public class LoanService : ILoanService
{
    private readonly PeerLendDbContext _context;
    private readonly INotificationService _notificationService; // ◄ 1. Declare the private messaging field

    // 2. Inject INotificationService alongside the DbContext
    public LoanService(PeerLendDbContext context, INotificationService notificationService)
    {
        _context = context;
        _notificationService = notificationService;
    }

    public async Task<Loan> SubmitApplicationAsync(Guid borrowerId, long principalAmountKobo, int interestRateBps, int tenorDays, CancellationToken cancellationToken = default)
    {
        if (principalAmountKobo <= 0)
            throw new ArgumentException("Loan principal request allocation must be greater than zero kobo.");
        if (interestRateBps <= 0)
            throw new ArgumentException("Interest calculation metrics must be positive basis points values.");
        if (tenorDays <= 0)
            throw new ArgumentException("The total contract duration lifecycle timeframe must be greater than zero days.");

        var newLoan = new Loan
        {
            Id = Guid.NewGuid(),
            BorrowerId = borrowerId,
            PrincipalAmountKobo = principalAmountKobo,
            InterestRateBps = interestRateBps,
            TenorDays = tenorDays,
            Status = LoanStatus.ApplicationSubmitted
        };

        await _context.Loans.AddAsync(newLoan, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        Console.WriteLine($"[LOAN ENGINE SUCCESS] Formulated brand-new asset application under ID: {newLoan.Id} for Borrower: {borrowerId}");
        return newLoan;
    }

    public async Task<bool> UpdateLoanStatusAsync(Guid loanId, LoanStatus newStatus, CancellationToken cancellationToken = default)
    {
        var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
        if (loan == null)
        {
            Console.WriteLine($"[LOAN ENGINE CRITICAL] Status update rejected: Loan context under ID {loanId} could not be discovered.");
            return false;
        }

        try
        {
            Console.WriteLine($"[LOAN ENGINE STATE] Processing status mutation for Asset {loanId}: {loan.Status} ──► {newStatus}");

            loan.TransitionTo(newStatus);

            var affectedRows = await _context.SaveChangesAsync(cancellationToken);
            return affectedRows > 0;
        }
        catch (InvalidOperationException stateEx)
        {
            Console.WriteLine($"[LOAN ENGINE BLOCK] Core domain rejected illegal state transition attempt: {stateEx.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOAN ENGINE FAULT] Persistent storage failure executing transaction: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DisburseLoanAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
            if (loan == null || loan.Status != LoanStatus.Approved)
            {
                Console.WriteLine($"[DISBURSEMENT CRITICAL] Execution aborted: Loan asset {loanId} is missing or unapproved.");
                return false;
            }

            var borrowerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == loan.BorrowerId, cancellationToken);
            if (borrowerWallet == null) return false;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == loan.BorrowerId, cancellationToken);
            if (user == null) return false;

            var disbursementCreditRecord = new Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                WalletId = borrowerWallet.Id,
                AmountKobo = loan.PrincipalAmountKobo,
                Type = "CREDIT",
                Description = $"Loan Principal Disbursement - Asset Reference ID: {loan.Id}",
                Timestamp = DateTime.UtcNow
            };

            loan.TransitionTo(LoanStatus.Disbursed);
            loan.DisbursedAt = DateTime.UtcNow;
            loan.DueAt = DateTime.UtcNow.AddDays(loan.TenorDays);

            await _context.Transactions.AddAsync(disbursementCreditRecord, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            await dbTransaction.CommitAsync(cancellationToken);
            Console.WriteLine($"[DISBURSEMENT SETTLED] Capital transferred cleanly to Wallet: {borrowerWallet.Id}. Loan State: {loan.Status}");

            // 3. AUTOMATED DISBURSEMENT TRANSACTION TRIGGER (PL-58)
            // Fire communication milestones asynchronously immediately post-commit
            double principalInNaira = loan.PrincipalAmountKobo / 100.0;
            _ = _notificationService.SendEmailAsync(
                user.Email ?? "borrower@peerlend.com",
                "PeerLend Loan Disbursement Approved!",
                $"Good news! Your requested loan principal of NGN {principalInNaira:N2} has been successfully disbursed into your internal platform wallet container.",
                cancellationToken
            );

            return true;
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[CRITICAL DISBURSEMENT ABORT] Settle routine crashed. Full rollback executed: {ex.Message}");
            return false;
        }
    }
}