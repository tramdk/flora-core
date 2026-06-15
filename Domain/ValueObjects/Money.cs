using FloraCore.Domain.Common;

namespace FloraCore.Domain.ValueObjects;

/// <summary>
/// Value object representing a money amount with currency.
/// Immutable by design.
/// </summary>
public record Money(decimal Amount, string Currency = "VND")
{
    // ThrowIfNull
    public decimal Amount { get; init; } = Amount >= 0 ? Amount : throw new ArgumentException(DomainErrors.Money.AmountCannotBeNegative, nameof(Amount));
    public string Currency { get; init; } = !string.IsNullOrWhiteSpace(Currency) ? Currency.ToUpperInvariant() : throw new ArgumentException(DomainErrors.Money.CurrencyRequired, nameof(Currency));

    public static Money Zero(string currency = "VND") => new(0, currency);
    
    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException(DomainErrors.Money.CannotAddDifferentCurrencies);
        
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException(DomainErrors.Money.CannotSubtractDifferentCurrencies);
        
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator *(Money money, int multiplier)
    {
        return new Money(money.Amount * multiplier, money.Currency);
    }

    public override string ToString() => $"{Amount:N2} {Currency}";
}
