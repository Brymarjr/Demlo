namespace Demlo.Application.Common.Interfaces;

// Outlines core use cases for dispatching transactional platform communications.
// Enforces Section 12.1 fault-tolerant notification isolation standards.
public interface INotificationService
{
    // Dispatches a transactional email to a specific borrower destination.
    Task<bool> SendEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);

    // Dispatches a high-priority transactional SMS text message alert.
    Task<bool> SendSmsAsync(string toPhoneNumber, string message, CancellationToken cancellationToken = default);
}