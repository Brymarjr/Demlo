namespace Demlo.Application.Common.Interfaces;

// Outlines the background orchestration rules for analyzing a loan asset's risk parameters.
// Evaluates Layer 1 (Smile ID), Layer 2 (Mono Analytics), and Layer 3 (CRC Credit Bureau).
public interface IUnderwritingEngine
{
    // Evaluates an active loan request against compliance thresholds and credit registry data.
    // Dynamically mutates the loan's lifecycle status based on calculated risk thresholds.
    Task<bool> EvaluateLoanRiskAsync(Guid loanId, CancellationToken cancellationToken = default);
}