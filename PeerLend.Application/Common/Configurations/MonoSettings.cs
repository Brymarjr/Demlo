namespace PeerLend.Application.Common.Configurations;

// Maps securely to the MonoSettings block inside appsettings configurations.
// Enforces Section 7.1 platform parameters of the Engineering Bible.
public class MonoSettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}
