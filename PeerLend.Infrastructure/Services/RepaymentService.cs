using Microsoft.EntityFrameworkCore;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Domain.Enums;
using PeerLend.Infrastructure.Persistence;

namespace PeerLend.Infrastructure.Services;

// Implements the settlement engine tracking debt collections and ledger balance closures.
// Enforces Section 11.2 atomic transaction guardrails and dispatches receipt milestones.
public class RepaymentService : IRepaymentService
{
    private readonly PeerLendDbContext _context;
    private readonly ILoanService _loanService;
    private readonly INotificationService _notificationService; // ◄ 1. Declare the private messaging field

    // 2. Inject INotificationService into the constructor alongside existing dependencies
    public RepaymentService(
        PeerLendDbContext context,
        ILoanService loanService,
        INotificationService notificationService)
    {
        _context = context;
        _loanService = loanService;
        _notificationService = notificationService;
    }

    public async Task<long> CalculateOutstandingBalanceAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
        if (loan == null) return 0;

        double interestMultiplier = loan.InterestRateBps / 10000.0;
        long totalInterestKobo = (long)(loan.PrincipalAmountKobo * interestMultiplier);
        long totalPayableKobo = loan.PrincipalAmountKobo + totalInterestKobo;

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

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == loan.BorrowerId, cancellationToken);
            if (user == null) return false;

            long exactRemainingDebt = await CalculateOutstandingBalanceAsync(loanId, cancellationToken);
            if (exactRemainingDebt <= 0)
            {
                Console.WriteLine($"[REPAYMENT ABORT] Loan asset {loanId} is already fully settled.");
                return false;
            }

            long actualDeductionKobo = amountKobo > exactRemainingDebt ? exactRemainingDebt : amountKobo;

            long currentWalletBalance = await _context.Transactions
                .Where(t => t.WalletId == borrowerWallet.Id)
                .SumAsync(t => t.Type == "CREDIT" ? t.AmountKobo : -t.AmountKobo, cancellationToken);

            if (currentWalletBalance < actualDeductionKobo)
            {
                Console.WriteLine($"[REPAYMENT INSUFFICIENT] Borrower wallet lacks clear capital. Balance: {currentWalletBalance}, Needed: {actualDeductionKobo}");
                return false;
            }

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

            bool isFullySettled = (actualDeductionKobo == exactRemainingDebt);
            if (isFullySettled)
            {
                Console.WriteLine($"[REPAYMENT MATCH] Debt completely zeroed out. Closing down contract pipeline context.");
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.ClosedRepaid, cancellationToken);
            }

            await dbTransaction.CommitAsync(cancellationToken);
            Console.WriteLine($"[REPAYMENT SUCCESS] Liquidated {actualDeductionKobo} Kobo against Asset {loanId}.");

            // 3. AUTOMATED REPAYMENT TRANSACTION ALERT TRIGGER (PL-58)
            double repaidInNaira = actualDeductionKobo / 100.0;
            long postPaymentDebt = isFullySettled ? 0 : (exactRemainingDebt - actualDeductionKobo);
            double remainingInNaira = postPaymentDebt / 100.0;

            string smsPayloadText = isFullySettled
                ? $"PeerLend Alert: Repayment of NGN {repaidInNaira:N2} received! Your loan (ID: {loanId.ToString()[..8]}) is now FULLY SETTLED. Thank you!"
                : $"PeerLend Alert: Repayment of NGN {repaidInNaira:N2} received. Outstanding remaining balance: NGN {remainingInNaira:N2}.";

            _ = _notificationService.SendSmsAsync(user.PhoneNumber ?? "+2348000000000", smsPayloadText, cancellationToken);

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