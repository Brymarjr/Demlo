using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using PeerLend.Application.Common.Interfaces;

namespace PeerLend.Infrastructure.Services;

// Implements direct HTTP communications with the Termii SMS gateway.
// Enforces Section 6.2 automated OTP communication loops.
public class TermiiSmsService : ISmsService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public TermiiSmsService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<bool> SendVerificationOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["TermiiSettings:ApiKey"]
            ?? throw new InvalidOperationException("Termii API Key is unconfigured.");
        var senderId = _configuration["TermiiSettings:SenderId"] ?? "PeerLend";
        var baseUrl = _configuration["TermiiSettings:BaseUrl"] ?? "https://api.ng.termii.com";

        // Construct the request endpoint matching Termii's SMS delivery specification
        var requestUrl = $"{baseUrl.TrimEnd('/')}/api/sms/send";

        // Build the precise payload object demanded by Termii's JSON gateway
        var payload = new TermiiSmsRequest
        {
            ApiKey = apiKey,
            To = FormatPhoneNumber(phoneNumber),
            From = senderId,
            Sms = $"Your PeerLend verification code is: {otp}. Valid for 5 minutes. Do not share this code.",
            Type = "plain",
            Channel = "generic"
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(requestUrl, content, cancellationToken);

            // Returns true if Termii acknowledges the request with an HTTP 200/201 status code
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            // Log communication faults to server console while protecting pipeline flow
            Console.WriteLine($"[CRITICAL GATEWAY] Termii Network Failure: {ex.Message}");
            return false;
        }
    }

    // Standardizes local Nigerian number formats into the international standard E.164 pattern required by Termii (e.g., 2348030000000)
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