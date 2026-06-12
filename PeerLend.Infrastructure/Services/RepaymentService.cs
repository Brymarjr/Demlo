using Microsoft.EntityFrameworkCore;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Domain.Enums;
using PeerLend.Infrastructure.Persistence;

namespace PeerLend.Infrastructure.Services;

// Implements the settlement engine tracking debt collections and ledger balance closures.
// Enforces Section 11.2 atomic transaction guardrails to prevent double-spend anomalies.
public class RepaymentService : IRepaymentService
{
    private readonly PeerLendDbContext _context;
    private readonly ILoanService _loanService;

    public RepaymentService(PeerLendDbContext context, ILoanService loanService)
    {
        _context = context;
        _loanService = loanService;
    }

    public async Task<long> CalculateOutstandingBalanceAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
        if (loan == null) return 0;

        // Calculate total raw liability: Principal + simple daily interest parameter matching terms
        // Total Interest = Principal * (InterestRateBps / 10000)
        double interestMultiplier = loan.InterestRateBps / 10000.0;
        long totalInterestKobo = (long)(loan.PrincipalAmountKobo * interestMultiplier);
        long totalPayableKobo = loan.PrincipalAmountKobo + totalInterestKobo;

        // Fetch all successful repayments tracked under this loan asset context via description tag lookups
        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == loan.BorrowerId, cancellationToken);
        if (wallet == null) return totalPayableKobo;

        long totalRepaidKobo = await _context.Transactions
            .Where(t => t.WalletId == wallet.Id && t.Type == "DEBIT" && t.Description.Contains(loanId.ToString()))
            .SumAsync(t => t.AmountKobo, cancellationToken);

        long balanceRemaining = totalPayableKobo - totalRepaidKobo;
        return balanceRemaining < 0 ? 0 : balanceRemaining;
    }

    public async Task<bool> ProcessRepaymentAsync(Guid loanId, long amountKobo, CancellationToken cancellationToken = default)
    {
        if (amountKobo <= 0)
            throw new ArgumentException("Repayment allocation processing metric must be greater than zero kobo.");

        // 1. Initialize our atomic transactional framework boundary
        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
            if (loan == null || loan.Status != LoanStatus.Disbursed)
            {
                Console.WriteLine($"[REPAYMENT BLOCK] Asset {loanId} is missing or not currently in an active repayable state.");
                return false;
            }

            var borrowerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == loan.BorrowerId, cancellationToken);
            if (borrowerWallet == null) return false;

            // Compute current real-time balance outstanding to prevent over-collection errors
            long exactRemainingDebt = await CalculateOutstandingBalanceAsync(loanId, cancellationToken);
            if (exactRemainingDebt <= 0)
            {
                Console.WriteLine($"[REPAYMENT ABORT] Loan asset {loanId} is already fully settled.");
                return false;
            }

            // Cap the payment if the user provides an over-allocation amount
            long actualDeductionKobo = amountKobo > exactRemainingDebt ? exactRemainingDebt : amountKobo;

            // Fetch current real-time wallet ledger balances to ensure the user has sufficient funds
            long currentWalletBalance = await _context.Transactions
                .Where(t => t.WalletId == borrowerWallet.Id)
                .SumAsync(t => t.Type == "CREDIT" ? t.AmountKobo : -t.AmountKobo, cancellationToken);

            if (currentWalletBalance < actualDeductionKobo)
            {
                Console.WriteLine($"[REPAYMENT INSUFFICIENT] Borrower wallet lacks clear capital. Balance: {currentWalletBalance}, Needed: {actualDeductionKobo}");
                return false;
            }

            // 2. Add the double-entry DEBIT ledger record (Deducting funds out of user wallet)
            var paymentDebitRecord = new Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                WalletId = borrowerWallet.Id,
                AmountKobo = actualDeductionKobo,
                Type = "DEBIT",
                Description = $"Loan Repayment Settlement - Asset Reference ID: {loan.Id}",
                Timestamp = DateTime.UtcNow
            };

            await _context.Transactions.AddAsync(paymentDebitRecord, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            // 3. Evaluate state mutation triggers if the debt is completely cleared out
            if (actualDeductionKobo == exactRemainingDebt)
            {
                Console.WriteLine($"[REPAYMENT MATCH] Debt completely zeroed out. Closing down contract pipeline context.");
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.ClosedRepaid, cancellationToken);
            }

            await dbTransaction.CommitAsync(cancellationToken);
            Console.WriteLine($"[REPAYMENT SUCCESS] Liquidated {actualDeductionKobo} Kobo against Asset {loanId}.");
            return true;
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[REPAYMENT FAILURE] Settle handler tracking encountered internal faults. Rolled back: {ex.Message}");
            return false;
        }
    }
}