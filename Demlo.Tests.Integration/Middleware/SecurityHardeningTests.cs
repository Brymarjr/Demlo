using System.Net;
using Microsoft.AspNetCore.Http;
using StackExchange.Redis;
using Demlo.Application.Common.Interfaces;
using Demlo.Api.Middleware;
using Xunit;

namespace Demlo.Tests.Integration.Middleware;

public class SecurityHardeningTests
{
    [Fact]
    public async Task SecurityMiddleware_Should_TriggerAlert_And_Intercept_When_HoneypotIsTripped()
    {
        // 1. ARRANGE
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/admin/export-all-wallets"; // Target honeypot dummy path

        var mockPolicy = new MockGlobalPolicyEngine("60");
        var mockNotification = new MockNotificationService();
        
        // We can pass null safely for the redis parameter because the honeypot path 
        // short-circuits the pipeline before executing any Redis rate-limiting operations
        RequestDelegate next = (ctx) => Task.CompletedTask;
        var middleware = new SecurityHardeningMiddleware(next, null!);

        // 2. ACT
        await middleware.InvokeAsync(httpContext, mockPolicy, mockNotification);

        // 3. ASSERT
        Assert.True(mockNotification.WasSmsAlertDispatched); // Proves your tripwire alert fired
        Assert.Equal((int)HttpStatusCode.OK, httpContext.Response.StatusCode); // Dummy data packet returned
    }

    private class MockGlobalPolicyEngine : IGlobalPolicyEngine
    {
        private readonly string _val;
        public MockGlobalPolicyEngine(string val) => _val = val;
        public Task<string> GetPolicyValueAsync(string key, string def, CancellationToken token = default) => Task.FromResult(_val);
        public Task<bool> UpdatePolicyAsync(string k, string nv, string act, CancellationToken token = default) => Task.FromResult(true);
    }

    private class MockNotificationService : INotificationService
    {
        public bool WasSmsAlertDispatched { get; private set; }

        public Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
        {
            if (message.Contains("Honeypot tripped"))
            {
                WasSmsAlertDispatched = true;
            }
            return Task.FromResult(true);
        }

        public Task<bool> SendEmailAsync(string emailAddress, string subject, string body, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
}