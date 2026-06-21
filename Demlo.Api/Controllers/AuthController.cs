using Microsoft.AspNetCore.Mvc;
using Demlo.Application.Common.Interfaces;
using Demlo.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using Demlo.Infrastructure.Persistence;
using System.IdentityModel.Tokens.Jwt;
using Asp.Versioning; 
using Microsoft.AspNetCore.Authorization;
using Demlo.Application.Common;

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

    [HttpPost("rotate-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RotatePassword([FromBody] RotatePasswordDto request, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);

            if (user == null)
            {
                return Unauthorized(new { error = "User record could not be retrieved." });
            }

            if (!user.IsTemporaryPassword)
            {
                return BadRequest(new { error = "This account does not require a mandatory password rotation." });
            }

            // Verify the temporary password provided by the user matches the hash
            var isValidTempPassword = _securityService.VerifyPassword(request.TemporaryPassword, user.PasswordHash);
            if (!isValidTempPassword)
            {
                return Unauthorized(new { error = "Invalid temporary credentials." });
            }

            // Enforce basic password strength rules before hashing
            if (request.NewPassword.Length < 8)
            {
                return BadRequest(new { error = "New password must be at least 8 characters long." });
            }

            // Hash the new password and flip the invariant state
            user.PasswordHash = _securityService.HashPassword(request.NewPassword);
            user.IsTemporaryPassword = false;
            user.UpdatedAt = DateTime.UtcNow;

            _context.Users.Update(user);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Credential rotation successful. You may now log in with your new password." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL PASSWORD ROTATION FAULT] {ex.Message}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred during password rotation." });
        }
    }

    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto request, CancellationToken cancellationToken)
    {
        var normalizedPhone = PhoneUtil.NormalizePhoneNumber(request.PhoneNumber);

        // Executes the actual notification dispatch and Redis caching
        await _userService.GenerateAndSendPasswordResetOtpAsync(normalizedPhone, cancellationToken);

        // Returns OK universally to prevent enumeration attacks
        return Ok(new { message = "If the phone number is registered, an OTP has been sent." });
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto request, CancellationToken cancellationToken)
    {
        var normalizedPhone = PhoneUtil.NormalizePhoneNumber(request.PhoneNumber);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == normalizedPhone, cancellationToken);

        if (user == null)
        {
            return BadRequest(new { error = "Invalid request." });
        }

        // Executes the strict Redis cache validation
        var isOtpValid = await _userService.VerifyPasswordResetOtpAsync(normalizedPhone, request.Otp, cancellationToken);
        if (!isOtpValid)
        {
            return BadRequest(new { error = "Invalid or expired OTP." });
        }

        if (request.NewPassword.Length < 8)
        {
            return BadRequest(new { error = "Password must be at least 8 characters long." });
        }

        // Enforce the credential update and write to PostgreSQL
        user.PasswordHash = _securityService.HashPassword(request.NewPassword);
        user.LastPasswordChangedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Password has been successfully reset. You may now log in." });
    }

    [Authorize] 
    [HttpPost("me/change-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request, CancellationToken cancellationToken)
    {
        var userIdString = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdString, out Guid userId))
        {
            return Unauthorized(new { error = "Invalid token claims." });
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null) return Unauthorized();

        var isOldPasswordValid = _securityService.VerifyPassword(request.OldPassword, user.PasswordHash);
        if (!isOldPasswordValid)
        {
            return BadRequest(new { error = "The current password provided is incorrect." });
        }

        if (request.NewPassword.Length < 8)
        {
            return BadRequest(new { error = "New password must be at least 8 characters long." });
        }

        user.PasswordHash = _securityService.HashPassword(request.NewPassword);
        user.LastPasswordChangedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Your password has been successfully updated." });
    }
}