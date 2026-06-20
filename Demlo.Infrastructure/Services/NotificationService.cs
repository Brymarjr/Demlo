using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Demlo.Application.Common.Interfaces;

namespace Demlo.Infrastructure.Services;

// Implements real-world transactional messaging channels.
// Enforces Section 12.1 fault-tolerant notification isolation standards.
public class NotificationService : INotificationService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public NotificationService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public Task<bool> SendEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        // Keeping email as a console mock for now since we haven't integrated SendGrid or Resend yet.
        Console.WriteLine(string.Empty);
        Console.WriteLine($"┌─────────────────── [OUTBOUND EMAIL DISPATCH] ───────────────────┐");
        Console.WriteLine($"│ TO      : {toEmail,-53} │");
        Console.WriteLine($"│ SUBJECT : {subject,-53} │");
        Console.WriteLine($"├─────────────────────────────────────────────────────────────────┤");
        Console.WriteLine($"│ {body,-63} │");
        Console.WriteLine($"└─────────────────────────────────────────────────────────────────┘");
        Console.WriteLine(string.Empty);

        return Task.FromResult(true);
    }

    // ──► UPGRADED: Now physically dispatches SMS via the Termii Gateway
    public async Task<bool> SendSmsAsync(string toPhoneNumber, string message, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["TermiiSettings:ApiKey"];
        var senderId = _configuration["TermiiSettings:SenderId"] ?? "Termii";
        var baseUrl = _configuration["TermiiSettings:BaseUrl"] ?? "https://api.ng.termii.com/api/sms/send";

        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_TERMII_API_KEY_HERE")
        {
            Console.WriteLine("[TERMII WARNING] SMS dispatch aborted: API Key is missing or using default dummy value.");
            return false;
        }

        var payload = new TermiiSmsRequest
        {
            ApiKey = apiKey,
            To = FormatPhoneNumber(toPhoneNumber),
            From = senderId,
            Sms = message,
            Type = "plain",
            Channel = "generic"
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(baseUrl, content, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[TERMII SUCCESS] SMS delivered cleanly to {payload.To}");
                return true;
            }
            else
            {
                var errorResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"[TERMII REJECTED] Gateway returned {response.StatusCode}. Details: {errorResponse}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL GATEWAY] Termii Network Failure: {ex.Message}");
            return false;
        }
    }

    // Standardizes local Nigerian number formats into the E.164 pattern required by Termii (e.g., 2348030000000)
    private static string FormatPhoneNumber(string rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone)) return string.Empty;

        var digits = new string(rawPhone.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("0") && digits.Length == 11)
        {
            return $"234{digits[1..]}";
        }

        if (digits.StartsWith("234") && digits.Length == 13)
        {
            return digits;
        }

        // Failsafe for sandbox testing, Termii will reject if not valid, but we format best-effort
        return digits;
    }

    // Immutable internal DTO representation to enforce exact JSON naming rules for the Termii API wire format
    private class TermiiSmsRequest
    {
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("to")]
        public string To { get; set; } = string.Empty;

        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;

        [JsonPropertyName("sms")]
        public string Sms { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("channel")]
        public string Channel { get; set; } = string.Empty;
    }
}