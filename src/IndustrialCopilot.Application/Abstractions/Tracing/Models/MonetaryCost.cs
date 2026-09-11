namespace IndustrialCopilot.Application.Abstractions.Tracing.Models;

/// <summary>Known cost in an explicitly named currency. Null at the call site means unknown; zero means known zero.</summary>
public sealed record MonetaryCost
{
    public decimal Amount { get; }
    /// <summary>Uppercase three-letter currency code; the accounting producer must supply a valid currency.</summary>
    public string Currency { get; }

    public MonetaryCost(decimal amount, string currency)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        ArgumentNullException.ThrowIfNull(currency);
        if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Currency must be an uppercase three-letter code.", nameof(currency));
        Amount = amount;
        Currency = currency;
    }

    public MonetaryCost Add(MonetaryCost other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency) throw new ArgumentException("Different currencies cannot be added.", nameof(other));
        return new(checked(Amount + other.Amount), Currency);
    }
}
