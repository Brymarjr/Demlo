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

                services.AddScoped<ILoanService, LoanService>();
                services.AddScoped<IGlobalPolicyEngine, GlobalPolicyEngine>();
                services.AddScoped<INotificationService, NotificationService>();

                // ──► 1. REGISTER THE MATCHING JOB SERVICE IN THE DI CONTAINER
                services.AddScoped<LoanMatchingJob>();

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

        using (var serviceScope = host.Services.CreateScope())
        {
            var recurringJobManager = serviceScope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

            Console.WriteLine("[HANGFIRE] Scheduling Recurring Nightly Delinquency Audit Execution Rule...");
            recurringJobManager.AddOrUpdate<LoanDelinquencyJob>(
                "nightly-loan-delinquency-audit",
                job => job.RunDailyAuditAsync(CancellationToken.None),
                Cron.Daily(23, 59)
            );

            // ──► 2. WIRE UP THE 5-MINUTE RECURRING MATCHING ENGINE DAEMON
            Console.WriteLine("[HANGFIRE] Scheduling Automated Capital Allocation Matching Engine Loop (Every 5 Minutes)...");
            recurringJobManager.AddOrUpdate<LoanMatchingJob>(
                "automated-capital-allocation-matching",
                job => job.RunMatchingCycleAsync(CancellationToken.None),
                "*/5 * * * *" // Strict standard 5-minute cron descriptor expressions
            );
        }

        Console.WriteLine("Workers engine running smoothly. Press Ctrl+C to safely terminate processing pipelines.");

        await host.RunAsync();
    }
}