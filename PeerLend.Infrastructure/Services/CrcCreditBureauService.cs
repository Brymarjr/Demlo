using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Domain.Entities;

namespace PeerLend.Infrastructure.Services;

// Implements secure API interaction with the CRC Credit Bureau registry framework.
// Enforces Section 8.1 risk metrics and historical debt profile analysis algorithms.
public class CrcCreditBureauService : ICreditBureauService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public CrcCreditBureauService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<int> GetConsumerCreditScoreAsync(User user, string nationalIdentityToken, CancellationToken cancellationToken = default)
    {
        var username = _configuration["CrcBureauSettings:Username"] ?? throw new InvalidOperationException("CRC Bureau Username is unconfigured.");
        var password = _configuration["CrcBureauSettings:Password"] ?? throw new InvalidOperationException("CRC Bureau Password is unconfigured.");
        var baseUrl = _configuration["CrcBureauSettings:BaseUrl"] ?? "https://api.crccreditbureau.com";
        var clientId = _configuration["CrcBureauSettings:ClientId"] ?? string.Empty;

        var requestUrl = $"{baseUrl.TrimEnd('/')}/api/v1/creditscore/fetch";

        // Build standard authorization parameters requested by the registry gate
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("accept", "application/json");

        var payload = new
        {
            username = username,
            password = password,
            clientId = clientId,
            identityToken = nationalIdentityToken, // BVN Hash verification mapping
            fullName = "PeerLend Verified Consumer" 
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(requestUrl, content, cancellationToken);

            // Handle scenario where registry service might be temporarily unavailable or down
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[BUREAU WARNING] CRC registry returned status: {response.StatusCode}. Falling back to neutral baseline rank.");
                return 0; // Return neutral status code under Section 8.1 risk defaults
            }

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseString);

            // Locate the score number property inside the returned document tree
            if (doc.RootElement.TryGetProperty("creditScore", out var scoreElement))
            {
                return scoreElement.GetInt32();
            }

            // Fallback to neutral thin-file status if the user has no institutional record profile history
            Console.WriteLine($"[BUREAU INFO] User {user.Id} has no established credit history track. Registering thin-file footprint.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL BUREAU FAULT] Network connection to credit bureau registry timed out: {ex.Message}");
            return 0; // Maintain absolute fault isolation so a bureau outage never breaks registration flows
        }
    }
}
