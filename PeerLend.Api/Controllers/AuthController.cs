using Microsoft.AspNetCore.Mvc;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using PeerLend.Infrastructure.Persistence;

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

    // Exposes the gateway endpoint to process inbound OTP verification requests (PL-19)
    [HttpPost("verify-otp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto request, CancellationToken cancellationToken)
    {
        try
        {
            // Forward verification request to our service layer
            var isVerified = await _userService.VerifyOtpAsync(request, cancellationToken);

            if (isVerified)
            {
                return Ok(new { message = "Phone number verified successfully. Onboarding sequence unlocked." });
            }

            return BadRequest(new { error = "Verification processing failed." });
        }
        catch (ArgumentException ex)
        {
            // Catches invalid/mismatched code inputs
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Catches cache timeout or missing sequence records
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL DEFAULT] OTP Verification Failure: {ex}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred during phone verification." });
        }
    }
}