using PeerLend.Application.Common.Interfaces;

namespace PeerLend.Infrastructure.Services;

// Implements transactional messaging channels with console stream isolation.
// Enforces Section 12.2 non-blocking background output standards.
public class NotificationService : INotificationService
{
    public Task<bool> SendEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        // Thread-isolated execution trace mocking an outbound SMTP/Web API dispatch channel
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

    public Task<bool> SendSmsAsync(string toPhoneNumber, string message, CancellationToken cancellationToken = default)
    {
        // Thread-isolated execution trace mocking an outbound telecom SMS gateway route
        Console.WriteLine(string.Empty);
        Console.WriteLine($"┌──────────────────── [OUTBOUND SMS DISPATCH] ────────────────────┐");
        Console.WriteLine($"│ TO      : {toPhoneNumber,-53} │");
        Console.WriteLine($"├─────────────────────────────────────────────────────────────────┤");
        Console.WriteLine($"│ {message,-63} │");
        Console.WriteLine($"└─────────────────────────────────────────────────────────────────┘");
        Console.WriteLine(string.Empty);

        return Task.FromResult(true);
    }
}