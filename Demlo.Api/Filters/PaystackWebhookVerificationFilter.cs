using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Demlo.Api.Filters;

public class PaystackWebhookVerificationFilter : IAsyncActionFilter
{
    private readonly IConfiguration _configuration;

    public PaystackWebhookVerificationFilter(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        if (!request.Headers.TryGetValue("x-paystack-signature", out var incomingSignature))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Missing mandatory x-paystack-signature security header." });
            return;
        }

        string? secretKey = _configuration["PaystackSettings:SecretKey"];
        if (string.IsNullOrEmpty(secretKey))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
            return;
        }

        request.EnableBuffering();
        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        string rawRequestBody = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        byte[] keyBytes = Encoding.UTF8.GetBytes(secretKey);
        byte[] bodyBytes = Encoding.UTF8.GetBytes(rawRequestBody);
        
        using var hmac = new HMACSHA512(keyBytes);
        byte[] computedHashBytes = hmac.ComputeHash(bodyBytes);
        string computedSignature = Convert.ToHexString(computedHashBytes).ToLower();

        if (!string.Equals(computedSignature, incomingSignature.ToString().Trim(), StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid Paystack signature verification match." });
            return;
        }

        await next();
    }
}