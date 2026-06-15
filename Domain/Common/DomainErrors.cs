namespace FloraCore.Domain.Common;

/// <summary>
/// Domain-level error messages. Keeps the Domain layer pure without depending on Application interfaces.
/// </summary>
public static class DomainErrors
{
    /// <summary>
    /// Errors related to Money value object.
    /// </summary>
    public static class Money
    {
        public const string AmountCannotBeNegative = "Amount cannot be negative";
        public const string CurrencyRequired = "Currency is required";
        public const string CannotAddDifferentCurrencies = "Cannot add different currencies";
        public const string CannotSubtractDifferentCurrencies = "Cannot subtract different currencies";
    }
}
