using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis; // ◄ Imports the core connection channel
using Hangfire;
using Hangfire.Redis.StackExchange;
using Hangfire.Redis; 
using PeerLend.Application.Common.Interfaces;
using PeerLend.Infrastructure.Persistence;
using PeerLend.Infrastructure.Services;
using PeerLend.Workers.Jobs;

namespace PeerLend.Workers;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Initializing PeerLend Background Workers Process Engine...");

        // Establish the optimized, persistent multiplexer engine to manage the Redis memory pools
        var redisConnection = ConnectionMultiplexer.Connect("localhost:6379");

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                string connectionString = "Host=localhost;Database=peerlend_db;Username=postgres;Password=postgres";

                services.AddDbContext<PeerLendDbContext>(options =>
                    options.UseNpgsql(connectionString, b => b.MigrationsAssembly("PeerLend.Infrastructure")));

                services.AddScoped<ILoanService, LoanService>();
                services.AddScoped<IGlobalPolicyEngine, GlobalPolicyEngine>();
                services.AddScoped<INotificationService, NotificationService>();

                // Configure Hangfire Server and activate the Redis storage engine wrapper
                services.AddHangfire(config =>
                {
                    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                          .UseSimpleAssemblyNameTypeSerializer()
                          .UseRecommendedSerializerSettings()
                          // Passes the connection object explicitly to bypass resolution errors
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
        }

        Console.WriteLine("Workers engine running smoothly. Press Ctrl+C to safely terminate processing pipelines.");

        await host.RunAsync();
    }
}