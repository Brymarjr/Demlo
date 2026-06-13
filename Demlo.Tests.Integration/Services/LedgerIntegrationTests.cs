using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Entities;
using Demlo.Domain.Enums;
using Demlo.Infrastructure.Persistence;
using Demlo.Infrastructure.Services;
using Xunit;

namespace Demlo.Tests.Integration.Services;

public class LedgerIntegrationTests
{
    // Generates an isolated, uniquely named data database memory context per test run execution
    private DemloDbContext GetInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<DemloDbContext>()
            .UseInMemoryDatabase(databaseName: $"Demlo_Test_{Guid.NewGuid()}")
            // ──► ADD THIS line to tell EF Core to bypass relational transaction errors during unit tests
            .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new DemloDbContext(options);
    }

    [Fact]
    public async Task TransferFundsAsync_Should_Enforce_Strict_Liquidity_Guardrails_And_Prevent_Overdrafts()
    {
        // 1. ARRANGE: Set up isolated contexts and generate tracking data models
        var context = GetInMemoryDbContext();
        var walletService = new WalletService(context);

        var senderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();

        // Instantiate virtual wallets directly into the test database
        var senderWallet = new Wallet { Id = Guid.NewGuid(), UserId = senderId };
        var recipientWallet = new Wallet { Id = Guid.NewGuid(), UserId = recipientId };
        await context.Wallets.AddRangeAsync(senderWallet, recipientWallet);

        // Pre-fund the sender account with 10,000 Kobo (100 Naira) via an initial CREDIT transaction
        var startingCredit = new Transaction
        {
            Id = Guid.NewGuid(),
            WalletId = senderWallet.Id,
            AmountKobo = 10000,
            Type = "CREDIT",
            Description = "Initial Seed Capital Deposit",
            Timestamp = DateTime.UtcNow
        };
        await context.Transactions.AddAsync(startingCredit);
        await context.SaveChangesAsync();

        // 2. ACT: Attempt an illegal transfer exceeding available wallet balance (15,000 Kobo)
        var transferResult = await walletService.TransferFundsAsync(
            senderId,
            recipientId,
            15000,
            "Illegal High-Value P2P Outflow Test",
            CancellationToken.None
        );

        // 3. ASSERT: Verify the service correctly rejected the processing execution
        Assert.False(transferResult);

        // Confirm that no additional transaction ledger rows were recorded to disk
        var totalTransactionsCount = await context.Transactions.CountAsync();
        Assert.Equal(1, totalTransactionsCount); // Only our initial seed credit should exist
    }

    [Fact]
    public async Task EvaluateLoanRiskAsync_Should_Orchestrate_State_Waterfall_And_Approve_On_Valid_Credit()
    {
        // 1. ARRANGE: Build mock implementations of dependencies to isolate memory contexts
        var context = GetInMemoryDbContext();
        var loanService = new LoanService(context, new NotificationService());
        var mockBureauService = new MockCreditBureauService(750);

        // ──► ADD THIS: Inline mock to simulate the database returning a dynamic cutoff value of 550
        var mockPolicyEngine = new MockGlobalPolicyEngine("550");

        // Pass the 4th required argument straight into the constructor to clear error CS7036
        var underwritingEngine = new UnderwritingEngine(context, loanService, mockBureauService, mockPolicyEngine);

        var borrowerId = Guid.NewGuid();

        var user = new User { Id = borrowerId, BvnHash = "SHAHASH234234234", Email = "test@Demlo.com" };
        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();

        var loan = await loanService.SubmitApplicationAsync(borrowerId, 500000, 1500, 30, CancellationToken.None);

        // 2. ACT: Run the dynamic risk matrix validation pipeline
        var engineExecutionSuccess = await underwritingEngine.EvaluateLoanRiskAsync(loan.Id, CancellationToken.None);

        // 3. ASSERT: Verify structural correctness
        Assert.True(engineExecutionSuccess);

        var evaluatedLoanAsset = await context.Loans.FirstAsync(l => l.Id == loan.Id);
        Assert.Equal(LoanStatus.Approved, evaluatedLoanAsset.Status);
    }

    // Lightweight mock companion helper matching IGlobalPolicyEngine requirements for testing insulation
    private class MockGlobalPolicyEngine : IGlobalPolicyEngine
    {
        private readonly string _valueToReturn;
        public MockGlobalPolicyEngine(string valueToReturn) => _valueToReturn = valueToReturn;

        public Task<string> GetPolicyValueAsync(string key, string defaultValue, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_valueToReturn);
        }

        public Task<bool> UpdatePolicyAsync(string key, string newValue, string adminActor, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }

    // Lightweight mock companion helper class matching ICreditBureauService requirements
    private class MockCreditBureauService : ICreditBureauService
    {
        private readonly int _scoreToReturn;
        public MockCreditBureauService(int scoreToReturn) => _scoreToReturn = scoreToReturn;

        public Task<int> GetConsumerCreditScoreAsync(User user, string bvn, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_scoreToReturn);
        }
    }
}