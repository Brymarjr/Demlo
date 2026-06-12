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
}