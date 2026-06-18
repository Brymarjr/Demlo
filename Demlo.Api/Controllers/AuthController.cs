using Microsoft.AspNetCore.Mvc;
using Demlo.Application.Common.Interfaces;
using Demlo.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using Demlo.Infrastructure.Persistence;
using System.IdentityModel.Tokens.Jwt;
using Asp.Versioning; 

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")] 
[Route("api/v{version:apiVersion}/auth")] 
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IJwtTokenService _tokenService;
    private readonly DemloDbContext _context;
    private readonly ISecurityService _securityService;

    public AuthController(IUserService userService, IJwtTokenService tokenService, DemloDbContext context, ISecurityService securityService)
    {
        _userService = userService;
        _tokenService = tokenService;
        _context = context;
        _securityService = securityService;
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

    public class LoginPayload 
    { 
        public string Phone { get; set; } = string.Empty; 
        public string Password { get; set; } = string.Empty; 
    }

    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginPayload request, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Fetch user by their PhoneNumber property contract natively
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == request.Phone, cancellationToken);

            if (user == null)
            {
                return Unauthorized(new { success = false, error = new { code = "INVALID_CREDENTIALS", message = "Invalid phone number or password." } });
            }

            // 2. Enforce Verification Boundary Invariants
            if (string.IsNullOrEmpty(user.KycStatus) || user.KycStatus.ToUpper() == "PENDING")
            {
                return BadRequest(new { success = false, error = new { code = "PHONE_NOT_VERIFIED", message = "You must complete OTP verification before logging in." } });
            }

            // 3. Verified using your exact core SecurityService Enhanced Verification method matching the registration hashing strategy
            var isValidPassword = _securityService.VerifyPassword(request.Password, user.PasswordHash);

            if (!isValidPassword)
            {
                return Unauthorized(new { success = false, error = new { code = "INVALID_CREDENTIALS", message = "Invalid phone number or password." } });
            }

            // 4. Generate Security Token Exchange Infrastructure Parameters
            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            var tokenHandler = new JwtSecurityTokenHandler();
            var jwtToken = tokenHandler.ReadJwtToken(accessToken);
            
            // ──► FIX: Search using the standardized claim type property string
            var jti = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value 
                      ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "jti")?.Value 
                      ?? Guid.NewGuid().ToString();

            await _tokenService.SaveRefreshTokenAsync(user.Id, refreshToken, jti, cancellationToken);

            return Ok(new
            {
                success = true,
                data = new
                {
                    accessToken,
                    refreshToken,
                    userId = user.Id,
                    role = user.Role.ToString() 
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL LOGIN FAULT] Processing failure: {ex.Message}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected server fault occurred during authentication execution." });
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