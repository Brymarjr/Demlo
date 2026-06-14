using Microsoft.EntityFrameworkCore;
using Demlo.Domain.Common;
using Demlo.Domain.Entities;

namespace Demlo.Infrastructure.Persistence;

// The primary data context acting as the bridge to PostgreSQL.
// Enforces Section 4.1 and Section 4.2 of the Engineering Bible.
public class DemloDbContext : DbContext
{
    public DemloDbContext(DbContextOptions<DemloDbContext> options) : base(options)
    {
    }

    // --- Identity Sets ---
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<BorrowerProfile> BorrowerProfiles => Set<BorrowerProfile>();
    public DbSet<LenderProfile> LenderProfiles => Set<LenderProfile>();
    public DbSet<GlobalPolicy> GlobalPolicies => Set<GlobalPolicy>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<FinancialLedgerLog> FinancialLedgerLogs => Set<FinancialLedgerLog>();

    // --- Core Financial Sets ---
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<LoanAllocation> LoanAllocations => Set<LoanAllocation>();

    // --- Double-Entry Ledger Sets ---
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<LedgerReconciliationAudit> LedgerReconciliationAudits => Set<LedgerReconciliationAudit>();
    public DbSet<LenderRiskFundPool> LenderRiskFundPools => Set<LenderRiskFundPool>();
    public DbSet<LenderRiskFundLedger> LenderRiskFundLedgers => Set<LenderRiskFundLedger>();

    // Intercepts the persistence pipeline to enforce automated audit tracking metrics.
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
                // CRITICAL FINANCIAL GUARDRAIL: Prohibit updating core append-only ledger transaction rows
                if (entry.Entity is FinancialLedgerLog || entry.Entity is LenderRiskFundLedger)
                {
                    throw new InvalidOperationException($"Mutating immutable system tracking ledger rows of type '{entry.Entity.GetType().Name}' is unauthorized.");
                }

                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    // Configures advanced entity mapping rules and performance constraints.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply our global snake_case schema naming conventions across all generated tables
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
        modelBuilder.Entity<FinancialLedgerLog>().ToTable("financial_ledger_logs");
        modelBuilder.Entity<Wallet>().ToTable("wallets");
        modelBuilder.Entity<Transaction>().ToTable("transactions");
        modelBuilder.Entity<LedgerReconciliationAudit>().ToTable("ledger_reconciliation_audits");
        modelBuilder.Entity<LenderRiskFundPool>().ToTable("lender_risk_fund_pools");
        modelBuilder.Entity<LenderRiskFundLedger>().ToTable("lender_risk_fund_ledgers");

        // Enforce specific conversions and scaling constraints directly on backing models
        modelBuilder.Entity<Loan>()
            .Property(l => l.Status)
            .HasConversion<int>();
    }
}