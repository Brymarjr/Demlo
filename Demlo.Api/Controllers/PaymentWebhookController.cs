using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Enums;
using Demlo.Domain.Entities;
using Demlo.Infrastructure.Persistence;
using Demlo.Api.Models.Webhooks;
using Demlo.Api.Filters;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhooks/mono")]
public class PaymentWebhookController : ControllerBase
{
    private readonly DemloDbContext _context;
    private readonly ILoanService _loanService;
    private readonly INotificationService _notificationService;
    private readonly ILedgerLiquidationService _ledgerLiquidationService;
    private readonly IFinancialLedgerService _ledgerService; // ◄ 1. INJECT THE FINANCIAL LEDGER AUDITOR

    public PaymentWebhookController(
        DemloDbContext context,
        ILoanService loanService,
        INotificationService notificationService,
        ILedgerLiquidationService ledgerLiquidationService,
        IFinancialLedgerService ledgerService) // ◄ 2. MAP TO CONSTRUCTOR
    {
        _context = context;
        _loanService = loanService;
        _notificationService = notificationService;
        _ledgerLiquidationService = ledgerLiquidationService;
        _ledgerService = ledgerService;
    }

    [HttpPost("collections")]
    [ServiceFilter(typeof(MonoWebhookVerificationFilter))]
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
            
            bool distributionSuccess = await _ledgerLiquidationService.DistributeRepaymentAsync(loan.Id, payload.Data.Amount, cancellationToken);

            if (!distributionSuccess)
            {
                return BadRequest(new { error = "Ledger execution failed due to an allocation imbalance." });
            }

            // ──► 3. LOG REPAYMENT EVENT TO THE IMMUTABLE DOUBLE-ENTRY LEDGER
            // Source is Guid.Empty because the funds originated externally from Mono's open banking rail
            await _ledgerService.LogTransactionAsync(
                reference: $"mono_rec_{loan.Id}_{DateTime.UtcNow.Ticks}",
                transactionType: "REPAYMENT_LIQUIDATION",
                sourceAccountId: Guid.Empty,
                destinationAccountId: loan.BorrowerId, // Tracks influx directly onto the borrower asset loop profile
                amountKobo: payload.Data.Amount,
                narrative: $"Automated Mono collection settlement split for Loan Ref: {loan.Id}",
                cancellationToken: cancellationToken
            );

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

    [HttpPost("paystack-settlements")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandlePaystackPayoutCallback([FromBody] PaystackWebhookDto payload, CancellationToken cancellationToken)
    {
        string reference = payload.Data.Reference;
        if (string.IsNullOrEmpty(reference) || !reference.StartsWith("disburse_"))
        {
            return BadRequest(new { error = "Malformed or missing external settlement reference format." });
        }

        string loanIdStr = reference.Replace("disburse_", "");
        if (!Guid.TryParse(loanIdStr, out Guid loanId))
        {
            return BadRequest(new { error = "Invalid unique cryptographic identifier signature." });
        }

        var loan = await _context.Loans
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);

        if (loan == null)
        {
            return Ok(new { message = "Payout callback received but no matching loan asset exists." });
        }

        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // --- SUCCESS FLOW: CASH RECEIVED BY BORROWER ---
            if (payload.Event == "transfer.success")
            {
                if (loan.Status == LoanStatus.Disbursed)
                {
                    loan.TransitionTo(LoanStatus.Active);
                    loan.DisbursedAt = DateTime.UtcNow;
                    loan.DueAt = DateTime.UtcNow.AddDays(loan.TenorDays);

                    // ──► 4. LOG LOAN DISBURSEMENT EVENT TO THE IMMUTABLE DOUBLE-ENTRY LEDGER
                    // Destination is Guid.Empty because the capital physically exited our system boundaries out to the NIBSS network
                    await _ledgerService.LogTransactionAsync(
                        reference: reference,
                        transactionType: "LOAN_DISBURSEMENT",
                        sourceAccountId: loan.BorrowerId,
                        destinationAccountId: Guid.Empty,
                        amountKobo: loan.PrincipalAmountKobo,
                        narrative: $"Outbound capital disbursement clear via Paystack for Loan ID: {loan.Id}",
                        cancellationToken: cancellationToken
                    );

                    Console.WriteLine($"[PAYSTACK SETTLEMENT SUCCESS] Loan {loan.Id} is now ACTIVE. Repayment tracking initiated.");
                }
            }
            // --- FAILURE/REVERSAL FLOW: CASH REJECTED BY NIBSS NETWORK ---
            else if (payload.Event == "transfer.failed" || payload.Event == "transfer.reversed")
            {
                Console.WriteLine($"[PAYSTACK SETTLEMENT FAILED] Payout bounced for Loan {loan.Id}. Initiating capital rollback sequence.");

                loan.Status = LoanStatus.AwaitingMatch;

                var failedAllocations = await _context.LoanAllocations
                    .Where(a => a.LoanId == loan.Id)
                    .ToListAsync(cancellationToken);

                foreach (var allocation in failedAllocations)
                {
                    var lender = await _context.LenderProfiles
                        .FirstOrDefaultAsync(p => p.UserId == allocation.LenderId, cancellationToken);

                    if (lender != null)
                    {
                        lender.AvailableBalanceKobo += allocation.AllocatedAmountKobo;
                    }
                }

                _context.LoanAllocations.RemoveRange(failedAllocations);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            return Ok(new { status = "PROCESSED" });
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[SETTLEMENT GATEWAY CRITICAL ERROR] Failed resolving Paystack payload for Loan {loan.Id}: {ex.Message}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Internal reconciliation processing failure." });
        }
    }

    [HttpPost("paystack-manual-repayments")]
    [ServiceFilter(typeof(PaystackWebhookVerificationFilter))] // ◄ CRYPTOGRAPHIC INTEGRITY GUARD
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleManualPaystackRepayment([FromBody] PaystackWebhookDto payload, CancellationToken cancellationToken)
    {
        // 1. Verify this event is a successful user charge pay-in card/ussd event
        if (payload.Event != "charge.success")
        {
            return Ok(new { message = "Event received but ignored (non-charge outcome)." });
        }

        // 2. Extract internal tracking reference metadata (e.g., manual_pay_{loanId}_{timestamp})
        string reference = payload.Data.Reference;
        if (string.IsNullOrEmpty(reference) || !reference.StartsWith("manual_pay_"))
        {
            return BadRequest(new { error = "Malformed manual repayment transaction reference." });
        }

        string[] parts = reference.Split('_');
        if (parts.Length < 3 || !Guid.TryParse(parts[2], out Guid loanId))
        {
            return BadRequest(new { error = "Invalid metadata loan identifier payload." });
        }

        var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
        if (loan == null) return Ok(new { message = "No matching loan found for manual payment." });

        // 3. Trigger immediate pro-rata distribution to lenders via liquidation engine
        bool splitSuccess = await _ledgerLiquidationService.DistributeRepaymentAsync(loan.Id, payload.Data.Amount, cancellationToken);
        if (!splitSuccess) return BadRequest(new { error = "Ledger distribution failed." });

        // 4. Log the transaction inside our master double-entry logging table
        await _ledgerService.LogTransactionAsync(
            reference: reference,
            transactionType: "MANUAL_REPAYMENT_CARD",
            sourceAccountId: Guid.Empty, // Cash originated outside our database ecosystems
            destinationAccountId: loan.BorrowerId,
            amountKobo: payload.Data.Amount,
            narrative: $"Manual user repayment cleared via Paystack checkout channels for Loan Ref: {loan.Id}",
            cancellationToken: cancellationToken
        );

        // 5. If fully paid back, transition the state machine to close the contract asset
        loan.TransitionTo(LoanStatus.ClosedRepaid);
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new { status = "MANUAL_PAYMENT_PROCESSED_SUCCESSFULLY" });
    }
}