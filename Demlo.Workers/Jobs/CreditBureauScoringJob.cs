using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Enums;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Workers.Jobs;

public class CreditBureauScoringJob
{
    private readonly DemloDbContext _context;
    private readonly ICreditBureauService _creditBureauService;
    private readonly INotificationService _notificationService;
    private readonly IGlobalPolicyEngine _policyEngine;

    public CreditBureauScoringJob(
        DemloDbContext context,
        ICreditBureauService creditBureauService,
        INotificationService notificationService,
        IGlobalPolicyEngine policyEngine)
    {
        _context = context;
        _creditBureauService = creditBureauService;
        _notificationService = notificationService;
        _policyEngine = policyEngine;
    }

    public async Task ProcessPendingUnderwritingScoresAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[RISK ENGINE] Scanning for assets awaiting bureau scoring at: {DateTime.UtcNow}");

        // 1. Fetch dynamic passing threshold set by admins via the dashboard engine
        string minScorePolicyStr = await _policyEngine.GetPolicyValueAsync("MINIMUM_CRC_PASSING_SCORE", "600", cancellationToken);
        int minimumPassingScore = int.Parse(minScorePolicyStr);

        var pendingLoans = await _context.Loans
            .Where(l => l.Status == LoanStatus.CreditScoring)
            .ToListAsync(cancellationToken);

        if (!pendingLoans.Any()) return;

        foreach (var loan in pendingLoans)
        {
            // Fetch the root User record
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == loan.BorrowerId, cancellationToken);

            // Fetch the relational profile row to record credit adjustments
            var borrowerProfile = await _context.BorrowerProfiles
                .FirstOrDefaultAsync(bp => bp.UserId == loan.BorrowerId, cancellationToken);

            if (user == null || borrowerProfile == null)
            {
                Console.WriteLine($"[RISK ENGINE ERROR] Loan {loan.Id} missing complete user or borrower profiles. Skipping.");
                continue;
            }

            try
            {
                Console.WriteLine($"[RISK ENGINE] Dispatching bureau report query for user: {user.Email}");
                
                // 2. Invoke method matching your exact signature contract: GetConsumerCreditScoreAsync
                // We pass user, and use their email/phone identifier mapping token sequence
                int creditScore = await _creditBureauService.GetConsumerCreditScoreAsync(user, user.Email, cancellationToken);

                // Update the calculated score back to the sub-profile for dashboard visibility
                borrowerProfile.CreditScore = creditScore;

                Console.WriteLine($"[UNDERWRITING EVALUATION] Loan {loan.Id} -> Calculated Score: {creditScore} (Dynamic Min Required: {minimumPassingScore})");

                // 3. Execute risk validation logic against admin parameters
                if (creditScore >= minimumPassingScore)
                {
                    // PASS FLOW: Hand off to marketplace pool
                    loan.TransitionTo(LoanStatus.AwaitingMatch);
                    
                    string smsMessage = $"Good news! Your Demlo loan application of NGN {(loan.PrincipalAmountKobo / 100):N0} has passed credit screening and is now matching with lenders.";
                    _ = _notificationService.SendSmsAsync(user.PhoneNumber, smsMessage, cancellationToken);
                    
                    Console.WriteLine($"[UNDERWRITING AUTO-PASS] Loan {loan.Id} successfully cleared.");
                }
                else
                {
                    // FAIL FLOW: Hard rejection
                    loan.TransitionTo(LoanStatus.Rejected);
                    
                    string smsMessage = "Demlo Notice: We regret to inform you that your loan application could not be approved at this time based on credit bureau risk evaluation parameters.";
                    _ = _notificationService.SendSmsAsync(user.PhoneNumber, smsMessage, cancellationToken);
                    
                    Console.WriteLine($"[UNDERWRITING AUTO-DECLINE] Loan {loan.Id} rejected due to risk non-compliance.");
                }

                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RISK ENGINE CRITICAL ERROR] Failed processing underwriting for Loan {loan.Id}: {ex.Message}");
            }
        }
    }
}