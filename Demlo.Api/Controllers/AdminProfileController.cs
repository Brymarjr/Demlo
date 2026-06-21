using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Asp.Versioning;
using Demlo.Infrastructure.Persistence;
using Demlo.Domain.Entities;
using Demlo.Domain.Enums;
using Demlo.Application.DTOs; 
using Demlo.Application.Common.Interfaces; // ◄ Assumed abstraction for email dispatch
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Demlo.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/profiles")]
[Authorize(Roles = "Admin")] 
public class AdminProfileController : ControllerBase
{
    private readonly DemloDbContext _context;
    private readonly ISecurityService _securityService; 
    private readonly IEmailService _emailService;       

    public AdminProfileController(
        DemloDbContext context, 
        ISecurityService securityService, 
        IEmailService emailService)
    {
        _context = context;
        _securityService = securityService;
        _emailService = emailService;
    }

    [HttpPost("create-admin")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProfileNewAdmin([FromBody] CreateAdminDto request, CancellationToken cancellationToken)
    {
        if (await _context.Users.AnyAsync(u => u.Email == request.Email || u.PhoneNumber == request.PhoneNumber, cancellationToken))
        {
            return Conflict(new { error = "An administrator profile with this email or phone number already exists." });
        }

        string tempPassword = GenerateSecureTempPassword();
        string passwordHash = _securityService.HashPassword(tempPassword);

        var newAdmin = new User
        {
            Id = Guid.NewGuid(),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            PasswordHash = passwordHash,
            Role = UserRole.Admin,           
            KycStatus = "PENDING",
            // ◄ Enforce state invariants for credential rotation
            IsTemporaryPassword = true,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Users.AddAsync(newAdmin, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        // ◄ PRODUCTION RUNTIME: Explicitly dispatch credentials to the administrator securely
        string emailSubject = "Demlo Administrative Console Enrollment";
        string emailBody = $"Hello {request.FirstName},\n\nYour administrative account has been provisioned. Use the following temporary credentials to log in:\n\nTemporary Password: {tempPassword}\n\nUpon successful authentication, you will be required to execute an immediate credential rotation.\n\nRegards,\nDemlo Engineering";
        
        await _emailService.SendEmailAsync(newAdmin.Email, emailSubject, emailBody, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new {
            message = "Administrative profile successfully provisioned. Credentials dispatched to email securely.",
            adminId = newAdmin.Id
        });
    }

    private string GenerateSecureTempPassword()
    {
        byte[] randomBytes = new byte[8];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return "Adm!" + Convert.ToBase64String(randomBytes).Substring(0, 8);
    }
}