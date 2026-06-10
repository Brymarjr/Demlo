using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Infrastructure.Services;

namespace PeerLend.Infrastructure;

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
            options.InstanceName = "PeerLend_";
        });

        // Register the typed HttpClient pattern for Smile ID compliance validation.
        services.AddHttpClient<IKycService, SmileIdKycService>();

        // Register the typed HttpClient pattern for Mono Open Banking data retrieval.
        services.AddHttpClient<IOpenBankingService, MonoOpenBankingService>();

        return services;
    }
}