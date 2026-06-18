using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Demlo.Application.Common.Interfaces;
using Demlo.Application.DTOs;
using Asp.Versioning;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/profiles")]
[Authorize] // ◄ CRITICAL: Enforces that only users with a valid Access Token can hit these routes
public class ProfileController : ControllerBase
{
    private readonly IProfileService _profileService;

    public ProfileController(IProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpPut("borrower")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateBorrowerProfile([FromBody] UpdateBorrowerProfileDto request, CancellationToken cancellationToken)
    {
        try
        {
            // Cryptographically extract the User ID straight from the validated JWT Token
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { error = "Invalid or missing token identity payload." });
            }

            // Hand off the payload to the Application Layer brain
            await _profileService.UpdateBorrowerProfileAsync(userId, request, cancellationToken);

            return Ok(new { message = "Tier-1 borrower profile synchronized successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL DEFAULT] Profile Synchronization Failure: {ex.Message}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An unexpected fault occurred while saving profile metrics." });
        }
    }
}