using System.Net;
using System.Text.Json;
using StackExchange.Redis;
using Demlo.Application.Common.Interfaces;

namespace Demlo.Api.Middleware;

public class SecurityHardeningMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConnectionMultiplexer _redis;

    public SecurityHardeningMiddleware(RequestDelegate next, IConnectionMultiplexer redis)
    {
        _next = next;
        _redis = redis;
    }

    public async Task InvokeAsync(HttpContext context, IGlobalPolicyEngine policyEngine, INotificationService notificationService)
    {
        string requestPath = context.Request.Path.ToString().ToLower();
        string clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";

        // ──► 1. THE DEFENISVE HONEYPOT TRIPWIRE TRIGGER
        // This is a fake, highly tempting endpoint for malicious scanning tools.
        if (requestPath.Contains("/api/v1/admin/export-all-wallets") || requestPath.Contains("/api/v1/auth/debug-tokens"))
        {
            Console.WriteLine($"[HONEYPOT ALERT] Penetration scanner tripped dummy route '{requestPath}' from IP: {clientIp}!");

            // Trigger immediate high-priority admin alert via your communication system
            string alertSms = $"CRITICAL: Demlo Security Honeypot tripped from IP {clientIp} targeting administrative endpoint schemas.";
            _ = notificationService.SendSmsAsync("08012345678", alertSms, CancellationToken.None); // Replace with true admin emergency mobile string

            // Feed the attacker realistic-looking fake payload data to waste their time/resources
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";

            var honeyPayload = new[]
            {
                new { wallet_id = Guid.NewGuid(), ledger_code = "WAL_RESERVE_001", ledger_balance_kobo = 84500000000, holder = "SYSTEM_VAULT_BACKUP" },
                new { wallet_id = Guid.NewGuid(), ledger_code = "WAL_ESCROW_002", ledger_balance_kobo = 12900000000, holder = "DELETED_DEBUG_ACCOUNT" }
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(honeyPayload));
            return; // Terminate pipe immediately so it never touches your database
        }

        // ──► 2. DISTRIBUTED REDIS TOKEN-BUCKET RATE-LIMITER (THE SHIELD)
        // Fetch current maximum allowed request speed limits set dynamically by the admin dashboard
        string maxRequestsPolicyStr = await policyEngine.GetPolicyValueAsync("GLOBAL_RATE_LIMIT_PER_MINUTE", "60", context.RequestAborted);
        int maxRequestsPerMinute = int.Parse(maxRequestsPolicyStr);

        var db = _redis.GetDatabase();
        string redisTrackingKey = $"rate_limit:{clientIp}:{DateTime.UtcNow:yyyyMMddHHmm}";

        // Atomically increment the request tracking counter for this specific minute window inside Redis
        long currentRequestVelocityCount = await db.StringIncrementAsync(redisTrackingKey);

        if (currentRequestVelocityCount == 1)
        {
            // Set a clean sliding expiry window so stale rate limit trackers self-delete from Redis memory
            await db.KeyExpireAsync(redisTrackingKey, TimeSpan.FromMinutes(2));
        }

        if (currentRequestVelocityCount > maxRequestsPerMinute)
        {
            Console.WriteLine($"[RATE-LIMIT BLOCK] Throttling caller IP: {clientIp}. Current count: {currentRequestVelocityCount}/{maxRequestsPerMinute}");
            
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests; // HTTP 429
            context.Response.ContentType = "application/json";

            var throttlingPayload = new { error = "Too many platform requests. Rate-limiting threshold exceeded. Please try again shortly." };
            await context.Response.WriteAsync(JsonSerializer.Serialize(throttlingPayload));
            return;
        }

        await _next(context);
    }
}