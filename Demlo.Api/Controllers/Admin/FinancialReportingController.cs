using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Asp.Versioning;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Api.Controllers.Admin;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/financial-reports")]
public class FinancialReportingController : ControllerBase
{
    private readonly DemloDbContext _context;

    public FinancialReportingController(DemloDbContext context)
    {
        _context = context;
    }

    // Retrieves a structured historical summary of all nightly balancing runs for the admin dashboard grid.
    [HttpGet("reconciliations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistoricalReconciliationSummaries(CancellationToken cancellationToken)
    {
        var summaries = await _context.LedgerReconciliationAudits
            .OrderByDescending(a => a.AuditDate)
            .Select(a => new
            {
                a.Id,
                a.AuditDate,
                a.TotalDebitsKobo,
                a.TotalCreditsKobo,
                a.VarianceKobo,
                a.Status,
                a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(summaries);
    }

    // Streams the pre-compiled double-entry ledger logs as a downloadable binary CSV file attachment.
    [HttpGet("reconciliations/{id:guid}/download-csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadLedgerAuditCsv([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var auditRecord = await _context.LedgerReconciliationAudits
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (auditRecord == null || string.IsNullOrEmpty(auditRecord.CsvPayload))
        {
            return NotFound(new { error = "Requested ledger reconciliation audit record or cached CSV export payload was not found." });
        }

        // Convert the raw database text payload string into an on-the-fly UTF-8 byte stream array
        byte[] csvBytes = Encoding.UTF8.GetBytes(auditRecord.CsvPayload);
        
        string fileAttachmentName = $"demlo_ledger_audit_{auditRecord.AuditDate:yyyyMMdd}.csv";

        // Stream the file back over the HTTP pipe with standard Excel-compatible text/csv MIME headers
        return File(csvBytes, "text/csv", fileAttachmentName);
    }
}