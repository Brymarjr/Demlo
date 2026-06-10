namespace PeerLend.Application.DTOs;

// Defines the data boundary structure for token renewal and rotation requests.
public record TokenRequestDto
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
}
