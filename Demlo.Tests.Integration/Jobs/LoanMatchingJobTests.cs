using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Entities;
using Demlo.Domain.Enums;
using Demlo.Infrastructure.Persistence;
using Demlo.Workers.Jobs;
using Xunit;

namespace Demlo.Tests.Integration.Jobs;

public class LoanMatchingJobTests
{
    private DemloDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<DemloDbContext>()
            .UseInMemoryDatabase(databaseName: $"Demlo_Match_Test_{Guid.NewGuid()}")
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new DemloDbContext(options);
    }

    [Fact]
    public async Task RunMatchingCycleAsync_Should_Succeed_And_Allocate_Lender_Balances_Equally()
    {
        var context = GetInMemoryDbContext();
        var mockPolicyEngine = new MockGlobalPolicyEngine("500000"); // NGN 5,000 blocks
        var mockDisbursalService = new MockPaystackDisbursementService();
        
        var matchingJob = new LoanMatchingJob(context, mockPolicyEngine, mockDisbursalService);

        var loanId = Guid.NewGuid();
        var loan = new Loan
        {
            Id = loanId,
            BorrowerId = Guid.NewGuid(),
            PrincipalAmountKobo = 1500000,
            Status = LoanStatus.AwaitingMatch,
            TenorDays = 30
        };
        await context.Loans.AddAsync(loan);

        var lender1 = new LenderProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), AvailableBalanceKobo = 500000 };
        var lender2 = new LenderProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), AvailableBalanceKobo = 500000 };
        var lender3 = new LenderProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), AvailableBalanceKobo = 500000 };
        await context.LenderProfiles.AddRangeAsync(lender1, lender2, lender3);
        await context.SaveChangesAsync();

        await matchingJob.RunMatchingCycleAsync(CancellationToken.None);

        var updatedLoan = await context.Loans.FindAsync(loanId);
        Assert.NotNull(updatedLoan);
        Assert.Equal(LoanStatus.Matched, updatedLoan.Status);

        var totalRemainingLiquidity = await context.LenderProfiles.SumAsync(l => l.AvailableBalanceKobo);
        Assert.Equal(0, totalRemainingLiquidity);

        var allocationCount = await context.LoanAllocations.CountAsync(a => a.LoanId == loanId);
        Assert.Equal(3, allocationCount);
    }

    [Fact]
    public async Task RunMatchingCycleAsync_Should_Halt_And_Rollback_Balances_On_Liquidity_Starvation()
    {
        var context = GetInMemoryDbContext();
        var mockPolicyEngine = new MockGlobalPolicyEngine("500000");
        var mockDisbursalService = new MockPaystackDisbursementService();
        
        var matchingJob = new LoanMatchingJob(context, mockPolicyEngine, mockDisbursalService);

        var loanId = Guid.NewGuid();
        var loan = new Loan
        {
            Id = loanId,
            BorrowerId = Guid.NewGuid(),
            PrincipalAmountKobo = 1500000,
            Status = LoanStatus.AwaitingMatch,
            TenorDays = 30
        };
        await context.Loans.AddAsync(loan);

        var lender1 = new LenderProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), AvailableBalanceKobo = 500000 };
        var lender2 = new LenderProfile { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), AvailableBalanceKobo = 500000 };
        await context.LenderProfiles.AddRangeAsync(lender1, lender2);
        await context.SaveChangesAsync();

        // ACT
        await matchingJob.RunMatchingCycleAsync(CancellationToken.None);

        // ASSERT
        var updatedLoan = await context.Loans.FindAsync(loanId);
        Assert.NotNull(updatedLoan);
        Assert.Equal(LoanStatus.AwaitingMatch, updatedLoan.Status); // Asset stays in pool

        // ──► FIXED: Fetch fresh tracking state snapshots directly from the context
        var freshLender1 = await context.LenderProfiles.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lender1.Id);
        var freshLender2 = await context.LenderProfiles.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lender2.Id);

        // Assert that capital remains completely un-trapped and fully restored to 5,000 NGN (500,000 Kobo)
        Assert.Equal(500000, freshLender1!.AvailableBalanceKobo);
        Assert.Equal(500000, freshLender2!.AvailableBalanceKobo);

        var allocationsExist = await context.LoanAllocations.AnyAsync(a => a.LoanId == loanId);
        Assert.False(allocationsExist);
    }

    private class MockGlobalPolicyEngine : IGlobalPolicyEngine
    {
        private readonly string _valueToReturn;
        public MockGlobalPolicyEngine(string valueToReturn) => _valueToReturn = valueToReturn;
        public Task<string> GetPolicyValueAsync(string key, string defaultValue, CancellationToken cancellationToken = default) => Task.FromResult(_valueToReturn);
        public Task<bool> UpdatePolicyAsync(string key, string newValue, string adminActor, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private class MockPaystackDisbursementService : IPaystackDisbursementService
    {
        // ──► FIXED THE CONTRACT METHOD SIGNATURE MAPPING FOR RECONCILIATION
        public Task<bool> InitiateLoanDisbursementAsync(Guid loanId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
}