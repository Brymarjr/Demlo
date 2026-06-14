using Microsoft.EntityFrameworkCore;
using Demlo.Domain.Entities;
using Demlo.Infrastructure.Persistence;
using Demlo.Workers.Jobs;
using Xunit;

namespace Demlo.Tests.Integration.Jobs;

public class LedgerBalancingJobTests
{
    private DemloDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<DemloDbContext>()
            .UseInMemoryDatabase(databaseName: $"Demlo_Audit_Test_{Guid.NewGuid()}")
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new DemloDbContext(options);
    }

    [Fact]
    public async Task RunNightlyBalancingAuditAsync_Should_Detect_And_Flag_Ledger_Imbalances()
    {
        var context = GetInMemoryDbContext();
        var balancingJob = new LedgerBalancingJob(context);

        var brokenLedgerLog = new FinancialLedgerLog
        {
            Id = Guid.NewGuid(),
            TransactionReference = "malicious_asymmetric_leak",
            TransactionType = "WALLET_FUNDING",
            SourceAccountId = Guid.NewGuid(),
            DestinationAccountId = Guid.Empty,
            AmountKobo = 75000,
            CreatedAt = DateTime.UtcNow
        };
        await context.FinancialLedgerLogs.AddAsync(brokenLedgerLog);
        await context.SaveChangesAsync();

        await balancingJob.RunNightlyBalancingAuditAsync(CancellationToken.None);

        var auditRun = await context.LedgerReconciliationAudits.FirstOrDefaultAsync();
        Assert.NotNull(auditRun);
        Assert.Equal("UNBALANCED_WARN", auditRun.Status);
        Assert.NotEqual(0, auditRun.VarianceKobo);
        Assert.Contains("malicious_asymmetric_leak", auditRun.CsvPayload);
    }
}