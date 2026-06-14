using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Enums;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Infrastructure.Services;

public class PaystackDisbursementService : IPaystackDisbursementService
{
    private readonly HttpClient _httpClient;
    private readonly DemloDbContext _context;
    private readonly IConfiguration _configuration;

    public PaystackDisbursementService(
        HttpClient httpClient, 
        DemloDbContext context, 
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _context = context;
        _configuration = configuration;

        // Initialize standard Paystack authorization authorization headers
        string secretKey = _configuration["PaystackSettings:SecretKey"] ?? "sk_test_placeholder";
        _httpClient.BaseAddress = new Uri("https://api.paystack.co/");
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secretKey);
    }

    public async Task<bool> InitiateLoanDisbursementAsync(Guid loanId, CancellationToken cancellationToken)
    {
        // 1. Fetch the matched loan along with the borrower's bank details
        var loan = await _context.Loans
            .FirstOrDefaultAsync(l => l.Id == loanId && l.Status == LoanStatus.Matched, cancellationToken);

        if (loan == null) return false;

        var borrower = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == loan.BorrowerId, cancellationToken);
            
        if (borrower == null) return false;

        // Fallback to email identifier if your User entity stores names inside a sub-profile
        string accountName = borrower.Email ?? "Demlo Verified Borrower"; 

        // In a live system, bank account and bank code are pulled directly from the borrower's verified profile
        string bankCode = "058"; // Example: GTBank Code
        string accountNumber = "0123456789"; 

        try
        {
            // 2. STEP A: Generate a Paystack Transfer Recipient Token
            var recipientPayload = new
            {
                type = "nuban",
                name = accountName,
                account_number = accountNumber,
                bank_code = bankCode,
                currency = "NGN"
            };

            var recipientResponse = await _httpClient.PostAsJsonAsync("transferrecipient", recipientPayload, cancellationToken);
            if (!recipientResponse.IsSuccessStatusCode) return false;

            // Using System.Text.Json element parsing directly to clear the CS8600 nullable assignment warning completely
            var jsonResult = await recipientResponse.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>(cancellationToken);
            string? recipientCode = jsonResult?["data"]?["recipient_code"]?.ToString();

            if (string.IsNullOrEmpty(recipientCode)) return false;

            // 3. STEP B: Execute the Transfer using a secure, unique Idempotency Key
            string idempotencyKey = $"disburse_{loanId}";

            var transferPayload = new
            {
                source = "balance",
                amount = loan.PrincipalAmountKobo, 
                recipient = recipientCode,
                reason = $"Demlo Automated Disbursal Ref: {loanId}"
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, "transfer")
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(transferPayload), Encoding.UTF8, "application/json")
            };
            
            requestMessage.Headers.Add("X-Idempotency-Key", idempotencyKey);

            var transferResponse = await _httpClient.SendAsync(requestMessage, cancellationToken);
            
            if (transferResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"[PAYSTACK DISBURSEMENT INIT] Payout successfully requested from bank vault for asset: {loanId}");
                
                loan.TransitionTo(LoanStatus.Disbursed);
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PAYSTACK GATEWAY CRITICAL ERROR] Disbursal processing failed for Loan {loanId}: {ex.Message}");
            return false;
        }
    }
}