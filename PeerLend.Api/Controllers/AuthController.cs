using Microsoft.AspNetCore.Mvc;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Application.DTOs;

namespace PeerLend.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;

    // Inject our business use case orchestration engine via the DI container
    public AuthController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterUserDto request, CancellationToken cancellationToken)
    {
        try
        {
            // Forward the payload contract down to the Application layer handler
            var userId = await _userService.RegisterUserAsync(request, cancellationToken);

            // Return a structured, successful HTTP 201 response containing the tracking Guid
            return CreatedAtAction(nameof(Register), new { id = userId }, new { userId, message = "User registration initialized successfully." });
        }
        catch (ArgumentException ex)
        {
            // Catches validation faults such as invalid roles
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Catches operational constraint faults such as Email/Phone duplicate conflicts
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            // Print the unexpected system error to the console array for local debugging
            Console.WriteLine($"[CRITICAL DEFAULT] Registration Failure: {ex}");

            // Return a clean, masked security error to the client
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected error occurred during account provisioning." });
        }
    }
}