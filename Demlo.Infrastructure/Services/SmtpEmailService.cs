using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Demlo.Application.Common.Interfaces;

namespace Demlo.Infrastructure.Services;

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _configuration;

    public SmtpEmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        var host = _configuration["EmailSettings:Host"] ?? throw new InvalidOperationException("SMTP Host missing.");
        var portStr = _configuration["EmailSettings:Port"] ?? "587";
        var port = int.Parse(portStr);
        var username = _configuration["EmailSettings:Username"] ?? throw new InvalidOperationException("SMTP Username missing.");
        var password = _configuration["EmailSettings:Password"] ?? throw new InvalidOperationException("SMTP Password missing.");
        var fromAddress = _configuration["EmailSettings:FromAddress"] ?? "admin@demlo.com";

        using var client = new SmtpClient(host, port)
        {
            Credentials = new NetworkCredential(username, password),
            EnableSsl = true
        };

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(fromAddress, "Demlo Administration"),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        mailMessage.To.Add(toEmail);

        await client.SendMailAsync(mailMessage, cancellationToken);
    }
}