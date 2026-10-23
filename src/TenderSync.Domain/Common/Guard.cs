using System.Diagnostics.CodeAnalysis;

namespace TenderSync.Domain.Common;

/// <summary>Small argument guards that throw <see cref="DomainException"/> so callers get domain-level errors.</summary>
public static class Guard
{
    public static string NotEmpty([NotNull] string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} is required.");
        }

        return value.Trim();
    }

    public static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static int InRange(int value, int min, int max, string name)
    {
        if (value < min || value > max)
        {
            throw new DomainException($"{name} must be between {min} and {max}.");
        }

        return value;
    }
}
