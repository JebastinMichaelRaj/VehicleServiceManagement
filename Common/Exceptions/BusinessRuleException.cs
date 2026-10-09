namespace VehicleServiceManagement.Common.Exceptions;

/// <summary>
/// Raised when a request violates a business rule (e.g. overlapping booking, invalid state transition).
/// Its message is safe to display to the user.
/// </summary>
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}
