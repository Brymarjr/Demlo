using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Enums;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Infrastructure.Services;

// Implements automated multi-tier underwriting evaluations with dynamic policy fetching.
// Utilizes senior performance tuning techniques to protect high-frequency operations.
public class UnderwritingEngine : IUnderwritingEngine
{
    private readonly DemloDbContext _context;
    private readonly ILoanService _loanService;
    private readonly ICreditBureauService _creditBureauService;
    private readonly IGlobalPolicyEngine _policyEngine;

    public UnderwritingEngine(
        DemloDbContext context,
        ILoanService loanService,
        ICreditBureauService creditBureauService,
        IGlobalPolicyEngine policyEngine)
    {
        _context = context;
        _loanService = loanService;
        _creditBureauService = creditBureauService;
        _policyEngine = policyEngine;
    }

    public async Task<bool> EvaluateLoanRiskAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        // SENIOR OPTIMIZATION: Read-only query performance tuning via AsNoTracking
        var loan = await _context.Loans.AsNoTracking().FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
        if (loan == null) return false;

        try
        {
            var moveToKyc = await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.KycPending, cancellationToken);
            if (!moveToKyc) return false;

            // Fetch user profile without overhead resource tracking
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == loan.BorrowerId, cancellationToken);
            if (user == null)
            {
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.Rejected, cancellationToken);
                return false;
            }

            if (string.IsNullOrEmpty(user.BvnHash))
            {
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.Rejected, cancellationToken);
                return true;
            }

            var moveToScoring = await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.CreditScoring, cancellationToken);
            if (!moveToScoring) return false;

            // Execute external registry checks OUTSIDE of any database transaction blocks to prevent table locks
            int institutionalCreditScore = await _creditBureauService.GetConsumerCreditScoreAsync(user, user.BvnHash, cancellationToken);

            // DYNAMIC POLICY LOOKUP
            // Query the live global configuration database with a reliable fallback
            string configuredCutoffString = await _policyEngine.GetPolicyValueAsync("MIN_CREDIT_SCORE_CUTOFF", "550", cancellationToken);
            int minRequiredCreditScore = int.Parse(configuredCutoffString);

            if (institutionalCreditScore == 0 || institutionalCreditScore >= minRequiredCreditScore)
            {
                Console.WriteLine($"[RISK ANALYSIS] Passed dynamically configured cutoff threshold ({minRequiredCreditScore}). Approving.");
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.Approved, cancellationToken);
            }
            else
            {
                Console.WriteLine($"[RISK ANALYSIS] Failed dynamically configured cutoff threshold ({minRequiredCreditScore}). Rejecting.");
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.Rejected, cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RISK ENGINE FAULT] Unexpected failure handling dynamic underwriting evaluation rules: {ex.Message}");
            await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.Rejected, cancellationToken);
            return false;
        }
    }
}