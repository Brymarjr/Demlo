namespace PeerLend.Application.DTOs;

// Holds the resulting token payload returned after a successful login or session rotation.
public record TokenResponseDto
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
