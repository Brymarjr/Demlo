using Microsoft.EntityFrameworkCore;
using Demlo.Domain.Common;
using Demlo.Domain.Entities;

namespace Demlo.Infrastructure.Persistence;

// The primary data context acting as the bridge to PostgreSQL 18.
// Enforces Section 4.1 and Section 4.2 of the Engineering Bible.
public class DemloDbContext : DbContext
{
    public DemloDbContext(DbContextOptions<DemloDbContext> options) : base(options)
    {
    }

    // --- Identity Sets ---
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
    public DbSet<BorrowerProfile> BorrowerProfiles => Set<BorrowerProfile>();
    public DbSet<LenderProfile> LenderProfiles => Set<LenderProfile>();
    public DbSet<GlobalPolicy> GlobalPolicies { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<FinancialLedgerLog> FinancialLedgerLogs { get; set; } = null!;

    // --- Core Financial Sets ---
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<LoanAllocation> LoanAllocations => Set<LoanAllocation>();

    // --- Double-Entry Ledger Sets ---
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Demlo.Domain.Entities.Wallet> Wallets { get; set; } = null!;
    public DbSet<Demlo.Domain.Entities.Transaction> Transactions { get; set; } = null!;
    public DbSet<LedgerReconciliationAudit> LedgerReconciliationAudits { get; set; } = null!;

    // Intercepts the persistence pipeline to enforce automated audit tracking metrics.
    // Overrides standard SaveChanges behavior to guarantee baseline data correctness.
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<BaseEntity>();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    // Configures advanced entity mapping rules and performance constraints.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply our global schema naming conventions across all generated tables
        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<RefreshToken>().ToTable("refresh_tokens");
        modelBuilder.Entity<BorrowerProfile>().ToTable("borrower_profiles");
        modelBuilder.Entity<LenderProfile>().ToTable("lender_profiles");
        modelBuilder.Entity<Loan>().ToTable("loans");
        modelBuilder.Entity<LoanAllocation>().ToTable("loan_allocations");
        modelBuilder.Entity<LedgerAccount>().ToTable("ledger_accounts");
        modelBuilder.Entity<LedgerEntry>().ToTable("ledger_entries");
        modelBuilder.Entity<GlobalPolicy>().ToTable("global_policies");
        modelBuilder.Entity<AuditLog>().ToTable("audit_logs");

        // Enforce specific decimal precision and scaling constraints directly on backing models
        // For example, mapping Loan statuses explicitly to integer enumerations inside the engine.
        modelBuilder.Entity<Loan>()
            .Property(l => l.Status)
            .HasConversion<int>();
    }
}
