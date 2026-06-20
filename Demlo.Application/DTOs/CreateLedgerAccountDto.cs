namespace Demlo.Application.DTOs;

public class CreateLedgerAccountDto
{
    public Guid OwnerId { get; set; }
    public string AccountType { get; set; } = string.Empty; // ASSET, LIABILITY, etc.
}