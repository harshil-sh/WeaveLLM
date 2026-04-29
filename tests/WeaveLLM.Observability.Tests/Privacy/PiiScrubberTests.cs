using FluentAssertions;
using WeaveLLM.Observability.Privacy;
using Xunit;

namespace WeaveLLM.Observability.Tests.Privacy;

public class PiiScrubberTests
{
    // ── Email ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scrub_Email_IsReplaced()
    {
        var result = PiiScrubber.Scrub("Contact us at alice@example.com for details.");
        result.Should().Contain("[EMAIL]");
        result.Should().NotContain("alice@example.com");
    }

    [Fact]
    public void Scrub_MultipleEmails_AllReplaced()
    {
        var result = PiiScrubber.Scrub("From bob@test.org to charlie@domain.co.uk");
        result.Should().NotContain("bob@test.org");
        result.Should().NotContain("charlie@domain.co.uk");
        result.Should().Contain("[EMAIL]");
    }

    // ── UK Phone ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("+44 7911 123456")]
    [InlineData("07911 123456")]
    [InlineData("07911-123456")]
    public void Scrub_UkPhone_IsReplaced(string phone)
    {
        var result = PiiScrubber.Scrub($"Call me on {phone} please.");
        result.Should().Contain("[PHONE]");
        result.Should().NotContain(phone);
    }

    // ── US Phone ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("(555) 123-4567")]
    [InlineData("555.123.4567")]
    [InlineData("555-123-4567")]
    public void Scrub_UsPhone_IsReplaced(string phone)
    {
        var result = PiiScrubber.Scrub($"Reach out at {phone}.");
        result.Should().Contain("[PHONE]");
        result.Should().NotContain(phone);
    }

    // ── UK National Insurance ────────────────────────────────────────────────────

    [Theory]
    [InlineData("AB 12 34 56 C")]
    [InlineData("AB123456C")]
    public void Scrub_UkNiNumber_IsReplaced(string ni)
    {
        var result = PiiScrubber.Scrub($"NI number: {ni}");
        result.Should().Contain("[NI-NUMBER]");
        result.Should().NotContain(ni);
    }

    // ── Credit Card ──────────────────────────────────────────────────────────────

    [Fact]
    public void Scrub_ValidCreditCard_IsReplaced()
    {
        // Visa test number that passes Luhn check
        var result = PiiScrubber.Scrub("Card: 4532015112830366");
        result.Should().Contain("[CARD]");
        result.Should().NotContain("4532015112830366");
    }

    [Fact]
    public void Scrub_InvalidCreditCard_NotReplaced()
    {
        // 16 digits but fails Luhn
        var result = PiiScrubber.Scrub("Number: 1234567890123456");
        result.Should().NotContain("[CARD]");
        result.Should().Contain("1234567890123456");
    }

    [Fact]
    public void Scrub_ValidCreditCardWithSpaces_IsReplaced()
    {
        // Standard formatted Visa test card with spaces
        var result = PiiScrubber.Scrub("Card: 4532 0151 1283 0366");
        result.Should().Contain("[CARD]");
    }

    // ── IPv4 Address ─────────────────────────────────────────────────────────────

    [Fact]
    public void Scrub_IpAddress_IsReplaced()
    {
        var result = PiiScrubber.Scrub("Server at 192.168.1.100 timed out.");
        result.Should().Contain("[IP-ADDRESS]");
        result.Should().NotContain("192.168.1.100");
    }

    // ── JSON password fields ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"{""password"": ""supersecret""}", "password")]
    [InlineData(@"{""secret"": ""mytoken""}", "secret")]
    [InlineData(@"{""apiKey"": ""sk-abc123""}", "apiKey")]
    [InlineData(@"{""api_key"": ""key-xyz""}", "api_key")]
    public void Scrub_JsonPasswordField_IsRedacted(string json, string fieldName)
    {
        var result = PiiScrubber.Scrub(json);
        result.Should().Contain("[REDACTED]");
        result.Should().Contain(fieldName);
    }

    // ── Non-PII ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Scrub_NonPiiText_IsUnchanged()
    {
        const string text = "The quick brown fox jumps over the lazy dog.";
        var result = PiiScrubber.Scrub(text);
        result.Should().Be(text);
    }

    [Fact]
    public void Scrub_EmptyString_ReturnsEmpty()
    {
        PiiScrubber.Scrub(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void Scrub_NullEquivalent_ReturnsUnchanged()
    {
        // The method accepts string so we pass whitespace
        var result = PiiScrubber.Scrub("   ");
        result.Should().Be("   ");
    }
}
