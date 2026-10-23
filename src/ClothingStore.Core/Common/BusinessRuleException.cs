namespace ClothingStore.Core;

/// <summary>
/// Raised when an operation violates a business rule (insufficient stock, closed shift, etc.).
/// The message is safe to show directly to the cashier.
/// </summary>
public sealed class BusinessRuleException(string message) : Exception(message);
