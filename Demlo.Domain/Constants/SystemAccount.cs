using System;

namespace Demlo.Domain.Constants;

public static class SystemAccounts
{
    // Platform Escrow (Liability)
    public static readonly Guid PlatformEscrow = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Liquidity Reserve (Asset)
    public static readonly Guid LiquidityReserve = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // Platform Fee (Revenue)
    public static readonly Guid PlatformFee = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // Outstanding Loan (Asset)
    public static readonly Guid OutstandingLoan = Guid.Parse("44444444-4444-4444-4444-444444444444");
}