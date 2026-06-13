namespace Demlo.Application.Common.Configurations;

// Maps securely to the CrcBureauSettings configuration block inside appsettings files.
// Enforces Section 8.1 risk engine underwriting parameters of the Engineering Bible.
public class CrcBureauSettings
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
}
