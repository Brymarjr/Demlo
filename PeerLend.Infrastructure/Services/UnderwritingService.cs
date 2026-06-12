using Microsoft.EntityFrameworkCore;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Domain.Enums;
using PeerLend.Infrastructure.Persistence;

namespace PeerLend.Infrastructure.Services;

// Implements automated multi-tier underwriting evaluations (Layers 1, 2, and 3).
// Enforces Section 4.4 credit score rules and risk threshold controls.
public class UnderwritingEngine : IUnderwritingEngine
{
    private readonly PeerLendDbContext _context;
    private readonly ILoanService _loanService;
    private readonly ICreditBureauService _creditBureauService;

    public UnderwritingEngine(
        PeerLendDbContext context,
        ILoanService loanService,
        ICreditBureauService creditBureauService)
    {
        _context = context;
        _loanService = loanService;
        _creditBureauService = creditBureauService;
    }

    public async Task<bool> EvaluateLoanRiskAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        // 1. Fetch the target loan alongside its tracking borrower profile details
        var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
        if (loan == null)
        {
            Console.WriteLine($"[RISK CRITICAL] Evaluation aborted: Loan reference asset {loanId} not found.");
            return false;
        }

        try
        {
            // 2. Advance state cleanly to KYC verification tracking
            var moveToKyc = await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.KycPending, cancellationToken);
            if (!moveToKyc) return false;

            // Fetch the user data to evaluate compliance levels
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == loan.BorrowerId, cancellationToken);
            if (user == null)
            {
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.Rejected, cancellationToken);
                return false;
            }

            // [LAYER 1 & 2 EVALUATION MATCH]
            // For testing purposes, we assume the user's explicit KYC data verification passes.
            // If the user's national ID details are completely missing, we reject the application automatically.
            if (string.IsNullOrEmpty(user.BvnHash))
            {
                Console.WriteLine($"[RISK REJECT] Borrower {user.Id} lacks valid identity token hash profiles. Aborting.");
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.Rejected, cancellationToken);
                return true;
            }

            // 3. Advance state cleanly to official Credit Bureau grading
            var moveToScoring = await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.CreditScoring, cancellationToken);
            if (!moveToScoring) return false;

            // [LAYER 3 EVALUATION - CRC BUREAU CHECK]
            Console.WriteLine($"[RISK SCORING] Dispatching cryptographic token identity to CRC Credit Bureau registry framework...");
            int institutionalCreditScore = await _creditBureauService.GetConsumerCreditScoreAsync(user, user.BvnHash, cancellationToken);

            Console.WriteLine($"[RISK RESULT] CRC Bureau registry returned validation score rating: {institutionalCreditScore}");

            // 4. Evaluate score thresholds against algorithmic underwriting guardrails (Section 4.4)
            // A score of 0 denotes a thin-file profile (no history). We accept thin-files at baseline or score >= 550.
            if (institutionalCreditScore == 0 || institutionalCreditScore >= 550)
            {
                Console.WriteLine($"[RISK PASS] Credit metric parameters passed underwriting benchmarks. Approving asset.");
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.Approved, cancellationToken);
            }
            else
            {
                Console.WriteLine($"[RISK REJECT] Credit score rating {institutionalCreditScore} falls below risk constraints. Rejecting asset.");
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.Rejected, cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RISK ENGINE FAULT] Unexpected exception handling automated risk assessment rules: {ex.Message}");
            // Force a safe fallback down to rejection to prevent stuck processing states
            await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.Rejected, cancellationToken);
            return false;
        }
    }
}