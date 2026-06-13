using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Entities;

namespace Demlo.Infrastructure.Services;

// Implements secure HTTP integration with the Smile ID identity engine.
// Enforces Section 6.3 asynchronous regulatory validation pipelines.
public class SmileIdKycService : IKycService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public SmileIdKycService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<bool> VerifyBvnAsync(User user, string rawBvn, CancellationToken cancellationToken = default)
    {
        return await SendSmileIdIdentityVerificationAsync(user, rawBvn, "BVN", cancellationToken);
    }

    public async Task<bool> VerifyNinAsync(User user, string rawNin, CancellationToken cancellationToken = default)
    {
        return await SendSmileIdIdentityVerificationAsync(user, rawNin, "NIN", cancellationToken);
    }

    private async Task<bool> SendSmileIdIdentityVerificationAsync(User user, string idNumber, string idType, CancellationToken cancellationToken)
    {
        var partnerId = _configuration["SmileIdSettings:PartnerId"] ?? throw new InvalidOperationException("Smile ID Partner ID is unconfigured.");
        var apiKey = _configuration["SmileIdSettings:ApiKey"] ?? throw new InvalidOperationException("Smile ID API Key is unconfigured.");
        var baseUrl = _configuration["SmileIdSettings:BaseUrl"] ?? "https://sandbox.smileidentity.com";
        var callbackUrl = _configuration["SmileIdSettings:CallbackUrl"];

        var requestUrl = $"{baseUrl.TrimEnd('/')}/v1/id_verification";

        // Generate the mandatory cryptographic security payload required by Smile ID
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var signature = GenerateSmileIdSignature(timestamp, partnerId, apiKey);

        var payload = new SmileIdIdentityRequest
        {
            PartnerId = partnerId,
            Timestamp = timestamp,
            Signature = signature,
            IdType = idType,
            IdNumber = idNumber,
            CallbackUrl = callbackUrl ?? string.Empty,
            PartnerParams = new PartnerTrackingParams
            {
                JobId = Guid.NewGuid().ToString(),
                UserId = user.Id.ToString(),
                JobType = 5 // Smile ID Standard Job Type 5 represents Enhanced ID Verification
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(requestUrl, content, cancellationToken);

            // Return true if Smile ID accepts the payload and queues the background verification job
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL GATEWAY] Smile ID Network Failure: {ex.Message}");
            return false;
        }
    }

    // Calculates an HMAC-SHA256 signature hash to verify request authenticity
    private static string GenerateSmileIdSignature(string timestamp, string partnerId, string apiKey)
    {
        var messageBytes = Encoding.UTF8.GetBytes($"{timestamp}{partnerId}sid_request");
        var keyBytes = Encoding.UTF8.GetBytes(apiKey);

        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(messageBytes);

        return Convert.ToBase64String(hashBytes);
    }

    // Immutable internal models strictly matching the Smile ID wire specification format
    private class SmileIdIdentityRequest
    {
        [JsonPropertyName("partner_id")]
        public string PartnerId { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; set; } = string.Empty;

        [JsonPropertyName("signature")]
        public string Signature { get; set; } = string.Empty;

        [JsonPropertyName("id_type")]
        public string IdType { get; set; } = string.Empty;

        [JsonPropertyName("id_number")]
        public string IdNumber { get; set; } = string.Empty;

        [JsonPropertyName("callback_url")]
        public string CallbackUrl { get; set; } = string.Empty;

        [JsonPropertyName("partner_params")]
        public PartnerTrackingParams PartnerParams { get; set; } = null!;
    }

    private class PartnerTrackingParams
    {
        [JsonPropertyName("job_id")]
        public string JobId { get; set; } = string.Empty;

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("job_type")]
        public int JobType { get; set; }
    }
}
