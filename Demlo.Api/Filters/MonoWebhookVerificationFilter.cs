using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Demlo.Api.Filters;

public class MonoWebhookVerificationFilter : IAsyncActionFilter
{
    private readonly IConfiguration _configuration;

    public MonoWebhookVerificationFilter(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        // 1. Extract the incoming cryptographic signature header from Mono
        if (!request.Headers.TryGetValue("x-mono-signature", out var incomingSignature))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Missing mandatory x-mono-signature security header." });
            return;
        }

        // 2. Fetch your secure webhook secret key using your exact appsettings layout
        string? secretKey = _configuration["MonoSettings:WebhookSecret"];
        if (string.IsNullOrEmpty(secretKey) || secretKey == "YOUR_MONO_WEBHOOK_SECRET_HERE")
        {
            // For safety during local development testing, use a fallback if not configured on the dashboard yet
            secretKey = "test_mono_secret_key_88_demlo_production_grade";
        }

        // 3. Enable buffering so we can read the raw HTTP stream without breaking downstream model binding
        request.EnableBuffering();
        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        string rawRequestBody = await reader.ReadToEndAsync();
        
        // Reset the stream position pointer back to zero so the [FromBody] model binder can read it next
        request.Body.Position = 0;

        // 4. Compute the local HMAC-SHA512 hash using your secret key
        byte[] keyBytes = Encoding.UTF8.GetBytes(secretKey);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(rawRequestBody);
        
        using var hmac = new HMACSHA512(keyBytes);
        byte[] computedHashBytes = hmac.ComputeHash(bodyBytes);
        
        // Convert the bytes to a lower-case hexadecimal string representation
        string computedSignature = Convert.ToHexString(computedHashBytes).ToLower();

        // 5. Compare signatures securely. If they don't match, reject the request instantly
        if (!string.Equals(computedSignature, incomingSignature.ToString().Trim(), StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid webhook signature validation match. Core access denied." });
            return;
        }

        await next();
    }
}