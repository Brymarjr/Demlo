using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Demlo.Application.Common.Interfaces;
using Demlo.Infrastructure.Services;

namespace Demlo.Infrastructure;

// Provides a centralized registration container for Infrastructure services.
// Enforces clean architectural separation across system boot pipelines.
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register our cryptographic services with a Transient lifecycle.
        // This ensures a fresh, lightweight instance is created every time a request is processed (Transient).
        services.AddTransient<ISecurityService, SecurityService>();

        // Register our business orchestration service
        services.AddTransient<IUserService, UserService>();

        // Register our cryptographic identity token factor service
        services.AddTransient<IJwtTokenService, JwtTokenService>();

        // Register the typed HttpClient pattern for Termii integration.
        // Optimizes connection pooling and prevents socket leaks.
        services.AddHttpClient<ISmsService, TermiiSmsService>();

        // Register the StackExchange Redis distributed cache provider.
        // Fulfills Section 6.2 caching and expiration rules.
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration["RedisSettings:Url"] ?? "localhost:6379";
            options.InstanceName = "Demlo_";
        });

        // Register the typed HttpClient pattern for Smile ID compliance validation.
        services.AddHttpClient<IKycService, SmileIdKycService>();

        // Register the typed HttpClient pattern for Mono Open Banking data retrieval.
        services.AddHttpClient<IOpenBankingService, MonoOpenBankingService>();

        // Register the typed HttpClient pattern for institutional credit history score retrieval.
        services.AddHttpClient<ICreditBureauService, CrcCreditBureauService>();

        // Register the concrete Double-Entry Ledger Wallet Engine service infrastructure
        services.AddScoped<IWalletService, WalletService>();

        // Register the concrete Loan Application and State Machine engine tracking infrastructure
        services.AddScoped<ILoanService, LoanService>();

        // Register the concrete Multi-Tier Automated Risk Underwriting Engine processor
        services.AddScoped<IUnderwritingEngine, UnderwritingEngine>();

        // Register the concrete Repayment Engine tracking wallet debit recoveries
        services.AddScoped<IRepaymentService, RepaymentService>();

        // Register the concrete Notification Service tracking communication streams
        services.AddScoped<INotificationService, NotificationService>();

        // Register the dynamic Governance Configuration Engine tracking corporate audits
        services.AddScoped<IGlobalPolicyEngine, GlobalPolicyEngine>();

        // Register the high-throughput fractional multi-lender distribution engine
        services.AddScoped<ILedgerLiquidationService, LedgerLiquidationService>();

        // Register the typed HttpClient pattern for Paystack core payout execution
        services.AddHttpClient<IPaystackDisbursementService, PaystackDisbursementService>();

        return services;
    }
}