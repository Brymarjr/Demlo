using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Entities;

namespace Demlo.Infrastructure.Services;

// Implements secure HTTP integration with Mono Open Banking data aggregations.
// Enforces Section 7.1 algorithmic financial analysis flows.
public class MonoOpenBankingService : IOpenBankingService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public MonoOpenBankingService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<string> ExchangeAuthCodeForAccountIdAsync(string publicAuthCode, CancellationToken cancellationToken = default)
    {
        var secretKey = _configuration["MonoSettings:SecretKey"] ?? throw new InvalidOperationException("Mono Secret Key is unconfigured.");
        var baseUrl = _configuration["MonoSettings:BaseUrl"] ?? "https://api.withmono.com";

        var requestUrl = $"{baseUrl.TrimEnd('/')}/account/auth";

        // Setup Mono specific authorization custom header rules
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("mono-sec-key", secretKey);
        _httpClient.DefaultRequestHeaders.Add("accept", "application/json");

        var payload = new { code = publicAuthCode };
        var jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(requestUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Mono Gateway rejected code exchange execution. Status: {response.StatusCode}");
            }

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseString);

            // Extract the permanent account ID token identifier from the resulting JSON document tree
            var accountId = doc.RootElement.GetProperty("id").GetString();
            return accountId ?? throw new InvalidOperationException("Mono token exchange completed but returned an empty structural identifier.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL MONO FAULT] Token Handshake Network Failure: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> SynchronizeAccountTelemetryAsync(User user, string accountId, CancellationToken cancellationToken = default)
    {
        var secretKey = _configuration["MonoSettings:SecretKey"] ?? throw new InvalidOperationException("Mono Secret Key is unconfigured.");
        var baseUrl = _configuration["MonoSettings:BaseUrl"] ?? "https://api.withmono.com";

        // Mono standard route pattern to trigger an asynchronous pull request sequence for updates
        var requestUrl = $"{baseUrl.TrimEnd('/')}/accounts/{accountId}/sync";

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("mono-sec-key", secretKey);

        try
        {
            using var emptyContent = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(requestUrl, emptyContent, cancellationToken);

            // Returns true if Mono accepts the sync task and pushes it to their internal background workers
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL MONO FAULT] Telemetry Synchronization Trigger Failure: {ex.Message}");
            return false;
        }
    }

    // Extracts the real-world bank details from Mono's secure vault
    public async Task<(string AccountNumber, string BankName)> GetAccountDetailsAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var secretKey = _configuration["MonoSettings:SecretKey"] ?? throw new InvalidOperationException("Mono Secret Key is unconfigured.");
        var baseUrl = _configuration["MonoSettings:BaseUrl"] ?? "https://api.withmono.com";

        var requestUrl = $"{baseUrl.TrimEnd('/')}/accounts/{accountId}";

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("mono-sec-key", secretKey);
        _httpClient.DefaultRequestHeaders.Add("accept", "application/json");

        try
        {
            var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Mono Gateway rejected account identity fetch. Status: {response.StatusCode}");
            }

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseString);
            var accountElement = doc.RootElement.GetProperty("account");

            string accountNumber = accountElement.GetProperty("accountNumber").GetString() ?? string.Empty;
            string bankName = accountElement.GetProperty("institution").GetProperty("name").GetString() ?? string.Empty;

            return (accountNumber, bankName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL MONO FAULT] Account Identity Fetch Failure: {ex.Message}");
            throw;
        }
    }
}
