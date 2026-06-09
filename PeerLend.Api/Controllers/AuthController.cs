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

    // Inject both our business service and our token factory via Dependency Injection
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

            // 2. Fetch the newly created user record from the database context to read its tracked domain properties
            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

            if (user == null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Account provisioning completed but user record could not be retrieved." });
            }

            // 3. Generate a secure short-lived access token and a high-entropy refresh token
            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken();

            // NOTE: In the next milestone, we will build the persistence engine to store 
            // this refresh token inside a secure tracking table for Rotation validation.

            // 4. Return a structured HTTP 201 response containing the user Guid and authentication keys
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
}