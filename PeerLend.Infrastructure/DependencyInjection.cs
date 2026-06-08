using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        // This ensures a fresh, lightweight instance is created every time a request is processed.
        services.AddTransient<ISecurityService, SecurityService>();

        // Register our business orchestration service
        services.AddTransient<IUserService, UserService>();

        return services;
    }
}