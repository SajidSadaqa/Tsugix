using Tsugix.Core;

namespace Tsugix.ContextEngine;

/// <summary>
/// Orchestrates the context extraction process.
/// Takes a CrashReport from Phase 1 and produces an ErrorContext for Phase 3.
/// </summary>
public interface IContextEngine
{
    /// <summary>
    /// Processes a crash report and extracts full error context.
    /// </summary>
    /// <param name="crashReport">The crash report from Phase 1.</param>
    /// <returns>Structured error context, or null if parsing fails.</returns>
    ErrorContext? Process(CrashReport crashReport);
}
