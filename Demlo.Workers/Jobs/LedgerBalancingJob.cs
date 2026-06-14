using System.Text;
using Microsoft.EntityFrameworkCore;
using Demlo.Infrastructure.Persistence;
using Demlo.Domain.Entities;

namespace Demlo.Workers.Jobs;

public class LedgerBalancingJob
{
    private readonly DemloDbContext _context;

    public LedgerBalancingJob(DemloDbContext context)
    {
        _context = context;
    }

    public async Task RunNightlyBalancingAuditAsync(CancellationToken cancellationToken)
    {
        DateTime processingWindow = DateTime.UtcNow.Date;
        Console.WriteLine($"[INTERNAL AUDITOR] Initializing ledger integrity sweep for window: {processingWindow:yyyy-MM-dd}");

        // 1. Query all ledger items recorded up to this point
        var entries = await _context.FinancialLedgerLogs
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        long totalDebits = 0;
        long totalCredits = 0;

        // 2. Initialize the CSV builder string with clean structural tracking headers
        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("TransactionId,Reference,Type,SourceAccount,DestinationAccount,AmountKobo,Timestamp");

        foreach (var entry in entries)
        {
            // For accounting tracking, entries moving out of a source account are debited 
            // and paths landing in a destination are credited.
            if (entry.SourceAccountId != Guid.Empty) totalDebits += entry.AmountKobo;
            if (entry.DestinationAccountId != Guid.Empty) totalCredits += entry.AmountKobo;

            // Append row values structured safely into the string builder
            csvBuilder.AppendLine($"{entry.Id},{entry.TransactionReference},{entry.TransactionType},{entry.SourceAccountId},{entry.DestinationAccountId},{entry.AmountKobo},{entry.CreatedAt:O}");
        }

        long variance = totalCredits - totalDebits;
        string auditStatus = variance == 0 ? "BALANCED" : "UNBALANCED_WARN";

        if (variance != 0)
        {
            Console.WriteLine($"[CRITICAL AUDIT ALERT] Ledger imbalance detected! Variance: {variance} Kobo. Flagging record.");
        }
        else
        {
            Console.WriteLine($"[AUDIT SUCCESS] Double-entry system is perfectly balanced. 0 Kobo variance deviation.");
        }

        // 3. Persist the reconciliation audit entry complete with the download-ready CSV payload
        var auditRecord = new LedgerReconciliationAudit
        {
            Id = Guid.NewGuid(),
            AuditDate = processingWindow,
            TotalDebitsKobo = totalDebits,
            TotalCreditsKobo = totalCredits,
            VarianceKobo = variance,
            Status = auditStatus,
            CsvPayload = csvBuilder.ToString(),
            CreatedAt = DateTime.UtcNow
        };

        await _context.LedgerReconciliationAudits.AddAsync(auditRecord, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}