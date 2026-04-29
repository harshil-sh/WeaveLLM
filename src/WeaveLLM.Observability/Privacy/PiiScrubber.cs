using System.Text.RegularExpressions;

namespace WeaveLLM.Observability.Privacy;

/// <summary>
/// Scrubs personally-identifiable information (PII) from text strings before logging or tracing.
/// All regex patterns are compiled once at startup for efficiency.
/// </summary>
public static partial class PiiScrubber
{
    [GeneratedRegex(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(\+44|0)[\s\-]?\d{4}[\s\-]?\d{6}", RegexOptions.Compiled)]
    private static partial Regex UkPhoneRegex();

    [GeneratedRegex(@"\(?\d{3}\)?[\s.\-]\d{3}[\s.\-]\d{4}", RegexOptions.Compiled)]
    private static partial Regex UsPhoneRegex();

    [GeneratedRegex(@"[A-Z]{2}\s?\d{2}\s?\d{2}\s?\d{2}\s?[A-D]", RegexOptions.Compiled)]
    private static partial Regex UkNiRegex();

    [GeneratedRegex(@"\b(?:\d[ \-]?){13,19}\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardRegex();

    [GeneratedRegex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled)]
    private static partial Regex IpAddressRegex();

    [GeneratedRegex(
        @"""(?:password|secret|apiKey|api_key)""\s*:\s*""[^""]*""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex JsonPasswordRegex();

    /// <summary>
    /// Replaces all detected PII patterns in <paramref name="text"/> with redaction tokens.
    /// </summary>
    /// <param name="text">The raw text to scrub.</param>
    /// <returns>A copy of <paramref name="text"/> with PII replaced by placeholder tokens.</returns>
    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        text = EmailRegex().Replace(text, "[EMAIL]");
        text = ScrubCreditCards(text);
        text = UkPhoneRegex().Replace(text, "[PHONE]");
        text = UsPhoneRegex().Replace(text, "[PHONE]");
        text = UkNiRegex().Replace(text, "[NI-NUMBER]");
        text = IpAddressRegex().Replace(text, "[IP-ADDRESS]");
        text = ScrubJsonPasswords(text);

        return text;
    }

    private static string ScrubCreditCards(string text)
    {
        return CreditCardRegex().Replace(text, m =>
            PassesLuhn(m.Value) ? "[CARD]" : m.Value);
    }

    private static string ScrubJsonPasswords(string text)
    {
        return JsonPasswordRegex().Replace(text, m =>
        {
            // Preserve the key name, redact the value: "password": "[REDACTED]"
            var colonIndex = m.Value.IndexOf(':');
            var keyPart = m.Value[..colonIndex].TrimEnd();
            return $"{keyPart}: \"[REDACTED]\"";
        });
    }

    private static bool PassesLuhn(string raw)
    {
        var digits = raw.Replace(" ", "").Replace("-", "");
        if (digits.Length < 13 || digits.Length > 19)
            return false;

        foreach (var ch in digits)
            if (!char.IsAsciiDigit(ch))
                return false;

        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9) n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }
}
