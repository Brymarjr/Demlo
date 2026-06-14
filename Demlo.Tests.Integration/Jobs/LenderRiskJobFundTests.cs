using Microsoft.EntityFrameworkCore;
using Demlo.Domain.Entities;
using Demlo.Domain.Enums;
using Demlo.Infrastructure.Persistence;
using Demlo.Workers.Jobs;
using Xunit;

namespace Demlo.Tests.Integration.Jobs;

public class LenderRiskFundJobTests
{
    private DemloDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<DemloDbContext>()
            .UseInMemoryDatabase(databaseName: $"Demlo_Lrf_Test_{Guid.NewGuid()}")
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new DemloDbContext(options);
    }

    [Fact]
    public async Task LiquidateDefaultedClaimsAsync_Should_BailoutLenders_When_ReservesAreSufficient()
    {
        // ARRANGE
        var context = GetInMemoryDbContext();
        var job = new LenderRiskFundJob(context);

        var pool = new LenderRiskFundPool { Id = Guid.NewGuid(), PoolCode = "LRF_MASTER_NGN", TotalReservesKobo = 5000000 };
        await context.LenderRiskFundPools.AddAsync(pool);

        var loanId = Guid.NewGuid();
        var loan = new Loan { Id = loanId, BorrowerId = Guid.NewGuid(), PrincipalAmountKobo = 1000000, Status = LoanStatus.Defaulted };
        await context.Loans.AddAsync(loan);

        var lenderProfileId = Guid.NewGuid();
        var lender = new LenderProfile { Id = lenderProfileId, UserId = Guid.NewGuid(), AvailableBalanceKobo = 0 };
        
        // ──► FIXED: Mapped LenderId and AllocatedAmountKobo to perfectly align with your entity architecture
        var allocation = new LoanAllocation 
        { 
            Id = Guid.NewGuid(), 
            LoanId = loanId, 
            LenderId = lenderProfileId, 
            AllocatedAmountKobo = 1000000 
        };
        
        await context.LenderProfiles.AddAsync(lender);
        await context.LoanAllocations.AddAsync(allocation);
        await context.SaveChangesAsync();

        // ACT
        await job.LiquidateDefaultedClaimsAsync(CancellationToken.None);

        // ASSERT
        var freshLender = await context.LenderProfiles.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lenderProfileId);
        Assert.Equal(1000000, freshLender!.AvailableBalanceKobo); 

        var freshPool = await context.LenderRiskFundPools.AsNoTracking().FirstOrDefaultAsync();
        Assert.Equal(4000000, freshPool!.TotalReservesKobo); 

        var freshLoan = await context.Loans.AsNoTracking().FirstOrDefaultAsync(l => l.Id == loanId);
        // ──► FIXED: Asset must transition cleanly to ClosedWrittenOff
        Assert.Equal(LoanStatus.ClosedWrittenOff, freshLoan!.Status); 
    }
}