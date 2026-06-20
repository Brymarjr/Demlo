using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Asp.Versioning;
using Demlo.Infrastructure.Persistence;
using Demlo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Demlo.Application.DTOs;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/ledger")]
[Authorize(Roles = "Admin")] // ◄ CRITICAL: Locks this down to administrative personnel only
public class AdminLedgerController : ControllerBase
{
    private readonly DemloDbContext _context;

    public AdminLedgerController(DemloDbContext context)
    {
        _context = context;
    }

    // Endpoint to dynamically create Chart of Accounts (COA)
    [HttpPost("account")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateLedgerAccount([FromBody] CreateLedgerAccountDto request)
    {
        var validTypes = new[] { "ASSET", "LIABILITY", "EQUITY", "REVENUE", "EXPENSE" };
        
        if (request is null || string.IsNullOrWhiteSpace(request.AccountType) || 
            !validTypes.Contains(request.AccountType.ToUpper()))
        {
            return BadRequest(new { error = $"Invalid request or account type. Must be one of: {string.Join(", ", validTypes)}" });
        }

        var newAccount = new LedgerAccount
        {
            OwnerId = request.OwnerId,
            AccountType = request.AccountType.ToUpper(),
            BalanceKobo = 0,
            CreatedAt = DateTime.UtcNow
        };

        await _context.LedgerAccounts.AddAsync(newAccount);
        await _context.SaveChangesAsync();

        return StatusCode(StatusCodes.Status201Created, new { 
            message = "Ledger account successfully registered.",
            account = newAccount
        });
    }

}