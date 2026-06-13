using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Enums;
using Demlo.Domain.Entities;
using Demlo.Infrastructure.Persistence;
using Demlo.Api.Models.Webhooks;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhooks/mono")]
public class PaymentWebhookController : ControllerBase
{
    private readonly DemloDbContext _context;
    private readonly ILoanService _loanService;
    private readonly INotificationService _notificationService;
    private readonly ILedgerLiquidationService _ledgerLiquidationService; // ◄ 1. Inject the liquidation engine

    public PaymentWebhookController(
        DemloDbContext context,
        ILoanService loanService,
        INotificationService notificationService,
        ILedgerLiquidationService ledgerLiquidationService)
    {
        _context = context;
        _loanService = loanService;
        _notificationService = notificationService;
        _ledgerLiquidationService = ledgerLiquidationService;
    }

    [HttpPost("collections")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleDirectDebitCallback([FromBody] MonoWebhookDto payload, CancellationToken cancellationToken)
    {
        var loan = await _context.Loans
            .FirstOrDefaultAsync(l => l.Status == LoanStatus.Disbursed || l.Status == LoanStatus.Overdue, cancellationToken);

        if (loan == null)
        {
            return Ok(new { message = "Webhook event received but no matching pending loan profile found." });
        }

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == loan.BorrowerId, cancellationToken);
        string borrowerPhone = user?.PhoneNumber ?? "+2348000000000";

        // --- TRACK PATH A: SUCCESSFUL REPAYMENT RECOVERY FLOW ---
        if (payload.Event == "direct-debit.successful")
        {
            Console.WriteLine($"[PAYMENT SUCCESS] Repayment cleared for Loan {loan.Id}. Triggering dynamic ledger split.");
            
            // 2. EXECUTING DEBIT RECOVERY LOGIC (Bible Section 9.3)
            // Distribute the incoming Kobo completely across all fractional lender allocations
            bool distributionSuccess = await _ledgerLiquidationService.DistributeRepaymentAsync(loan.Id, payload.Data.Amount, cancellationToken);

            if (!distributionSuccess)
            {
                return BadRequest(new { error = "Ledger execution failed due to an allocation imbalance." });
            }

            await _loanService.UpdateLoanStatusAsync(loan.Id, LoanStatus.Active, cancellationToken);
            return Ok(new { status = "SUCCESS", processed = true });
        }

        // --- TRACK PATH B: 3-STRIKE FAILURE CASCADE TIMELINE ---
        if (payload.Event == "direct-debit.failed")
        {
            int historicalFailures = await _context.AuditLogs
                .AsNoTracking()
                .CountAsync(log => log.EntityId == loan.Id && log.Action == "DIRECT_DEBIT_FAILURE_STRIKE", cancellationToken);

            int currentStrikeCount = historicalFailures + 1;

            var strikeAuditRecord = new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = Guid.Empty,
                EntityType = "LOAN_ASSET",
                EntityId = loan.Id,
                Action = "DIRECT_DEBIT_FAILURE_STRIKE",
                OldState = loan.Status.ToString(),
                NewState = loan.Status.ToString(),
                Ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
                CreatedAt = DateTime.UtcNow
            };
            await _context.AuditLogs.AddAsync(strikeAuditRecord, cancellationToken);

            if (currentStrikeCount == 1)
            {
                await _loanService.UpdateLoanStatusAsync(loan.Id, LoanStatus.Overdue, cancellationToken);
                string smsMessage = "Demlo Notice: Your automated repayment charge failed today. Your loan account has shifted to OVERDUE. Automatic retry scheduled in 48 hours.";
                _ = _notificationService.SendSmsAsync(borrowerPhone, smsMessage, cancellationToken);
            }
            else if (currentStrikeCount == 2)
            {
                string escalationMessage = "Demlo Urgent Warning: Your repayment collection has failed a second time. Final automated retry will execute on Day 7 to avoid formal default.";
                _ = _notificationService.SendSmsAsync(borrowerPhone, escalationMessage, cancellationToken);
            }
            else if (currentStrikeCount >= 3)
            {
                await _loanService.UpdateLoanStatusAsync(loan.Id, LoanStatus.Defaulted, cancellationToken);
                Console.WriteLine($"[LRF PROTECTION ACTIVATED] Calculating pro-rata lender protection compensation matrices for defaulted asset: {loan.Id}");
            }

            await _context.SaveChangesAsync(cancellationToken);
            return Ok(new { status = "FAILURE_PROCESSED", strike = currentStrikeCount });
        }

        return BadRequest(new { error = "Unsupported payment provider webhook channel action event." });
    }
}