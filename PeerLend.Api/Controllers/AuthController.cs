using Microsoft.AspNetCore.Mvc;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using PeerLend.Infrastructure.Persistence;
using System.IdentityModel.Tokens.Jwt;
using Asp.Versioning; // Added for API version tracking attributes

namespace PeerLend.Api.Controllers;

[ApiController]
[ApiVersion("1.0")] // Explicitly locks this controller to Version 1
[Route("api/v{version:apiVersion}/auth")] // Dynamically changes the route to api/v1/auth
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
            var userId = await _userService.RegisterUserAsync(request, cancellationToken);

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Account provisioning completed but user record could not be retrieved." });
            }

            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(accessToken);
            var jti = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value
                ?? Guid.NewGuid().ToString();

            await _tokenService.SaveRefreshTokenAsync(user.Id, refreshToken, jti, cancellationToken);

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

    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] TokenRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _userService.RefreshTokenAsync(request, cancellationToken);

            return Ok(result);
        }
        catch (Microsoft.IdentityModel.Tokens.SecurityTokenException ex)
        {
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