using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Hangfire;
using Hangfire.Redis.StackExchange;
using Demlo.Application.Common.Interfaces;
using Demlo.Infrastructure.Persistence;
using Demlo.Infrastructure.Services;
using Demlo.Workers.Jobs;

namespace Demlo.Workers;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Initializing Demlo Background Workers Process Engine...");

        var redisConnection = ConnectionMultiplexer.Connect("localhost:6379");

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                string connectionString = "Host=localhost;Database=Demlo_db;Username=postgres;Password=postgres";

                services.AddDbContext<DemloDbContext>(options =>
                    options.UseNpgsql(connectionString, b => b.MigrationsAssembly("Demlo.Infrastructure")));

                //  Core Infrastructure Services
                services.AddScoped<ILoanService, LoanService>();
                services.AddScoped<IGlobalPolicyEngine, GlobalPolicyEngine>();
                services.AddScoped<INotificationService, NotificationService>();
                
                //  Financial & Third-Party Integrations
                services.AddScoped<IFinancialLedgerService, FinancialLedgerService>();
                services.AddHttpClient<IPaystackDisbursementService, PaystackDisbursementService>();
                
                // NOTE: Register your concrete Phase 2 CRC implementation here if not handled inside your core Infrastructure extensions
                services.AddScoped<ICreditBureauService, Demlo.Infrastructure.Services.CrcCreditBureauService>();;

                // ──► 1. ALL JOB CLASSES REGISTERED IN THE DI CONTAINER
                services.AddScoped<LoanMatchingJob>();
                services.AddScoped<CreditBureauScoringJob>();
                services.AddScoped<LedgerBalancingJob>();
                services.AddScoped<LenderRiskFundJob>();

                // Configure Hangfire Storage
                services.AddHangfire(config =>
                {
                    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                          .UseSimpleAssemblyNameTypeSerializer()
                          .UseRecommendedSerializerSettings()
                          .UseRedisStorage(redisConnection);
                });

                services.AddHangfireServer(options =>
                {
                    options.WorkerCount = Environment.ProcessorCount * 2; 
                });
            })
            .Build();

        // ──► 2. SCHEDULE RECURRING BACKGROUND CRON DAEMONS
        using (var serviceScope = host.Services.CreateScope())
        {
            var recurringJobManager = serviceScope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

            // Nightly Delinquency Strike Audit (23:59)
            Console.WriteLine("[HANGFIRE] Scheduling Recurring Nightly Delinquency Audit Execution Rule...");
            recurringJobManager.AddOrUpdate<LoanDelinquencyJob>(
                "nightly-loan-delinquency-audit",
                job => job.RunDailyAuditAsync(CancellationToken.None),
                Cron.Daily(23, 59)
            );

            // Marketplace Matching Engine (Every 5 Minutes)
            Console.WriteLine("[HANGFIRE] Scheduling Automated Capital Allocation Matching Engine Loop (Every 5 Minutes)...");
            recurringJobManager.AddOrUpdate<LoanMatchingJob>(
                "automated-capital-allocation-matching",
                job => job.RunMatchingCycleAsync(CancellationToken.None),
                "*/5 * * * *"
            );

            // CRC Credit Bureau Underwriting Evaluator (Hourly)
            Console.WriteLine("[HANGFIRE] Scheduling Asynchronous CRC Bureau Scoring Risk Evaluation Engine (Hourly)...");
            recurringJobManager.AddOrUpdate<CreditBureauScoringJob>(
                "automated-crc-bureau-underwriting",
                job => job.ProcessPendingUnderwritingScoresAsync(CancellationToken.None),
                Cron.Hourly()
            );

            // Double-Entry Ledger Balancing Audit & CSV Cacher (23:45)
            Console.WriteLine("[HANGFIRE] Scheduling Nightly Double-Entry Ledger Balancing and Integrity Audit (23:45)...");
            recurringJobManager.AddOrUpdate<LedgerBalancingJob>(
                "nightly-ledger-balancing-audit",
                job => job.RunNightlyBalancingAuditAsync(CancellationToken.None),
                Cron.Daily(23, 45)
            );

            Console.WriteLine("[HANGFIRE] Scheduling Automated Daily Lender Risk Fund (LRF) Default Liquidation Insurance Engine (01:00)...");
            recurringJobManager.AddOrUpdate<LenderRiskFundJob>(
                "automated-lrf-default-liquidation",
                job => job.LiquidateDefaultedClaimsAsync(CancellationToken.None),
                Cron.Daily(1, 0)
             );
        }

        Console.WriteLine("Workers engine running smoothly. Press Ctrl+C to safely terminate processing pipelines.");

        await host.RunAsync();
    }
}