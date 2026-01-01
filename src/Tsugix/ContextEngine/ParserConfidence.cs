namespace Tsugix.ContextEngine;

/// <summary>
/// Confidence level for parser matching.
/// Higher values indicate stronger confidence that the parser can handle the input.
/// </summary>
public enum ParserConfidence
{
    /// <summary>
    /// Parser cannot handle this input.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Parser might be able to handle this input (weak indicators).
    /// </summary>
    Low = 1,
    
    /// <summary>
    /// Parser can likely handle this input (moderate indicators).
    /// </summary>
    Medium = 2,
    
    /// <summary>
    /// Parser is highly confident it can handle this input (strong indicators).
    /// </summary>
    High = 3
}
