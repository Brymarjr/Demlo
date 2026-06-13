using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using Demlo.Application.Common.Interfaces;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/policies")]
public class AdminPolicyController : ControllerBase
{
    private readonly IGlobalPolicyEngine _policyEngine;

    public AdminPolicyController(IGlobalPolicyEngine policyEngine)
    {
        _policyEngine = policyEngine;
    }

    // Mutates global platform operating rules dynamically and records entries into the audit tables (PL-61)
    [HttpPost("update")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> OverrideSystemPolicy(
        [FromQuery] string key,
        [FromQuery] string value,
        CancellationToken cancellationToken)
    {
        // Extract administrative actor context safely from tokens. 
        // Using an authorized fallback string for this specific implementation step.
        string adminActorUser = User.Identity?.Name ?? "system_admin_dashboard_user@Demlo.com";

        var updateSuccess = await _policyEngine.UpdatePolicyAsync(key, value, adminActorUser, cancellationToken);

        if (!updateSuccess)
        {
            return BadRequest(new { error = "Policy modification rejected. Review operational system logs for transactional deadlock indicators." });
        }

        return Ok(new { status = "Global rule parameters altered safely.", modifiedKey = key, runtimeValue = value });
    }
}