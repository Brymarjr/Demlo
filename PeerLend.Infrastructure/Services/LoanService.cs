using Microsoft.EntityFrameworkCore;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Domain.Entities;
using PeerLend.Domain.Enums;
using PeerLend.Infrastructure.Persistence;

namespace PeerLend.Infrastructure.Services;

// Implements the centralized state machine runner and lifecycle processor for active loans.
// Enforces Section 4.2 domain transition policies and prevents corrupt status jumps.
public class LoanService : ILoanService
{
    private readonly PeerLendDbContext _context;

    public LoanService(PeerLendDbContext context)
    {
        _context = context;
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
            Status = LoanStatus.ApplicationSubmitted // Locks down the initial lifecycle phase state
        };

        await _context.Loans.AddAsync(newLoan, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        Console.WriteLine($"[LOAN ENGINE SUCCESS] Formulated brand-new asset application under ID: {newLoan.Id} for Borrower: {borrowerId}");
        return newLoan;
    }

    public async Task<bool> UpdateLoanStatusAsync(Guid loanId, LoanStatus newStatus, CancellationToken cancellationToken = default)
    {
        // 1. Locate the target loan asset record inside our database persistence cluster
        var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
        if (loan == null)
        {
            Console.WriteLine($"[LOAN ENGINE CRITICAL] Status update rejected: Loan context under ID {loanId} could not be discovered.");
            return false;
        }

        try
        {
            Console.WriteLine($"[LOAN ENGINE STATE] Processing status mutation for Asset {loanId}: {loan.Status} ──► {newStatus}");

            // 2. Invoke the domain boundary method written inside your domain layer entity file.
            // This explicitly validates the transition rule matrix before updating memory states.
            loan.TransitionTo(newStatus);

            // 3. Persist the validated status update directly to PostgreSQL disk
            var affectedRows = await _context.SaveChangesAsync(cancellationToken);
            return affectedRows > 0;
        }
        catch (InvalidOperationException stateEx)
        {
            // Aborts smoothly if an illegal or corrupt status jump is attempted across the cluster
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
        // 1. Initialize an atomic database execution transaction context
        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // 2. Fetch the target loan asset and verify it is in a valid state for funding
            var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
            if (loan == null)
            {
                Console.WriteLine($"[DISBURSEMENT CRITICAL] Execution aborted: Loan asset {loanId} does not exist.");
                return false;
            }

            // Enforce state machine policy: Only approved loans can be funded
            if (loan.Status != LoanStatus.Approved)
            {
                Console.WriteLine($"[DISBURSEMENT REJECTED] Loan {loanId} cannot be disbursed. Current status: {loan.Status}");
                return false;
            }

            // 3. Locate the borrower's target virtual wallet container
            var borrowerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == loan.BorrowerId, cancellationToken);
            if (borrowerWallet == null)
            {
                Console.WriteLine($"[DISBURSEMENT FAULT] Target destination wallet missing for borrower: {loan.BorrowerId}");
                return false;
            }

            // 4. Generate the balancing double-entry transaction record
            // In a production P2P architecture, the debit would target an active investor pool or escrow system wallet token.
            // For this milestone implementation, we record a direct CREDIT entry into the borrower's wallet asset log.
            var disbursementCreditRecord = new Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                WalletId = borrowerWallet.Id,
                AmountKobo = loan.PrincipalAmountKobo,
                Type = "CREDIT",
                Description = $"Loan Principal Disbursement - Asset Reference ID: {loan.Id}",
                Timestamp = DateTime.UtcNow
            };

            // 5. Mutate the loan domain parameters safely through state transition rules
            loan.TransitionTo(LoanStatus.Disbursed);
            loan.DisbursedAt = DateTime.UtcNow;

            // Set the absolute due date deadline by adding tenor duration windows
            loan.DueAt = DateTime.UtcNow.AddDays(loan.TenorDays);

            // 6. Commit all changes to database persistence simultaneously
            await _context.Transactions.AddAsync(disbursementCreditRecord, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            // Commit transaction atomically to disk
            await dbTransaction.CommitAsync(cancellationToken);
            Console.WriteLine($"[DISBURSEMENT SETTLED] Capital transferred cleanly to Wallet: {borrowerWallet.Id}. Loan State: {loan.Status}");
            return true;
        }
        catch (Exception ex)
        {
            // Roll back the entire financial interaction if a connection timeout or disk fault surfaces
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[CRITICAL DISBURSEMENT ABORT] Settle routine crashed. Full rollback executed: {ex.Message}");
            return false;
        }
    }
}