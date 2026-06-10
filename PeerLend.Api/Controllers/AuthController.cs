using Microsoft.AspNetCore.Mvc;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using PeerLend.Infrastructure.Persistence;
using System.IdentityModel.Tokens.Jwt;

namespace PeerLend.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IJwtTokenService _tokenService;
    private readonly PeerLendDbContext _context;

    public AuthController(IUserService userService, IJwtTokenService tokenService, PeerLendDbContext context)
    {
        _userService = userService;
        _tokenService = tokenService;
        _context = context;
    }

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterUserDto request, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Forward the payload contract down to the Application layer handler
            var userId = await _userService.RegisterUserAsync(request, cancellationToken);

            // 2. Fetch the newly created user record from the database context
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Account provisioning completed but user record could not be retrieved." });
            }

            // 3. Generate initial authentication cryptographic keys
            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            // 4. Read the unforgeable JWT tracking identifier (JTI claim) out of our new access token string
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(accessToken);
            var jti = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value
                ?? Guid.NewGuid().ToString();

            // 5. Commit the refresh token session variables to PostgreSQL for Rotation tracking (PL-23)
            await _tokenService.SaveRefreshTokenAsync(user.Id, refreshToken, jti, cancellationToken);

            // 6. Return structured HTTP 201 response containing the user identifier and secure tracking keys
            return CreatedAtAction(nameof(Register), new { id = userId }, new
            {
                userId,
                accessToken,
                refreshToken,
                message = "User registration initialized successfully."
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL DEFAULT] Registration Failure: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred during account provisioning." });
        }
    }

    [HttpPost("verify-otp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto request, CancellationToken cancellationToken)
    {
        try
        {
            var isVerified = await _userService.VerifyOtpAsync(request, cancellationToken);

            if (isVerified)
            {
                return Ok(new { message = "Phone number verified successfully. Onboarding sequence unlocked." });
            }

            return BadRequest(new { error = "Verification processing failed." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL DEFAULT] OTP Verification Failure: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred during phone verification." });
        }
    }

    // Exposes the gateway endpoint to process token rotation and renewal requests (PL-25)
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] TokenRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            // Forward the payload down to our token exchange logic engine
            var result = await _userService.RefreshTokenAsync(request, cancellationToken);

            return Ok(result);
        }
        catch (Microsoft.IdentityModel.Tokens.SecurityTokenException ex)
        {
            // Catches token security violations, expiration, or replay attack warnings
            Console.WriteLine($"[SECURITY ALERT] Token Rotation Rejected: {ex.Message}");
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL DEFAULT] Token Refresh Failure: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred during session rotation." });
        }
    }
}