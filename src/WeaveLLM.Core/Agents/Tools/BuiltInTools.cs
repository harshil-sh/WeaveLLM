#nullable enable
using System.ComponentModel;

namespace WeaveLLM.Core.Agents.Tools;

/// <summary>
/// Provider interface for performing web searches.
/// Inject a concrete implementation and pass it to <see cref="WebSearchTool"/> to enable live search.
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>
    /// Executes a web search and returns the results as plain text.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task<string> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);
}

/// <summary>
/// Tool that returns the current UTC date and time.
/// </summary>
public sealed class DateTimeTool
{
    /// <summary>Returns the current UTC date and time in ISO 8601 round-trip format.</summary>
    [LLMTool("get_current_datetime", "Get the current UTC date and time")]
    public Task<string> GetCurrentDateTimeAsync() =>
        Task.FromResult(DateTime.UtcNow.ToString("O"));
}

/// <summary>
/// Tool that safely evaluates basic arithmetic expressions using a recursive descent parser.
/// Supports <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c>, parentheses, integers, and decimals.
/// </summary>
public sealed class CalculatorTool
{
    /// <summary>
    /// Evaluates a basic mathematical expression and returns the numeric result.
    /// Returns an error string for invalid or malformed expressions.
    /// </summary>
    /// <param name="expression">The arithmetic expression, e.g. <c>(12 + 4) * 3</c>.</param>
    [LLMTool("calculate", "Safely evaluate a basic math expression")]
    public Task<string> CalculateAsync(
        [Description("Expression e.g. (12 + 4) * 3")] string expression)
    {
        try
        {
            var parser = new ExpressionParser(expression);
            var value = parser.ParseExpression();
            parser.EnsureEnd();
            return Task.FromResult(value.ToString("G"));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    private sealed class ExpressionParser
    {
        private readonly string _input;
        private int _pos;

        public ExpressionParser(string input) => _input = input.Trim();

        public void EnsureEnd()
        {
            SkipWhitespace();
            if (_pos < _input.Length)
                throw new InvalidOperationException(
                    $"Unexpected character '{_input[_pos]}' at position {_pos}.");
        }

        // expr → term (('+' | '-') term)*
        public double ParseExpression()
        {
            var left = ParseTerm();
            SkipWhitespace();
            while (_pos < _input.Length && (_input[_pos] == '+' || _input[_pos] == '-'))
            {
                var op = _input[_pos++];
                var right = ParseTerm();
                left = op == '+' ? left + right : left - right;
                SkipWhitespace();
            }
            return left;
        }

        // term → factor (('*' | '/') factor)*
        private double ParseTerm()
        {
            var left = ParseFactor();
            SkipWhitespace();
            while (_pos < _input.Length && (_input[_pos] == '*' || _input[_pos] == '/'))
            {
                var op = _input[_pos++];
                var right = ParseFactor();
                if (op == '/' && right == 0)
                    throw new DivideByZeroException("Division by zero.");
                left = op == '*' ? left * right : left / right;
                SkipWhitespace();
            }
            return left;
        }

        // factor → '-'? (NUMBER | '(' expr ')')
        private double ParseFactor()
        {
            SkipWhitespace();
            if (_pos >= _input.Length)
                throw new InvalidOperationException("Unexpected end of expression.");

            if (_input[_pos] == '-')
            {
                _pos++;
                return -ParseFactor();
            }

            if (_input[_pos] == '(')
            {
                _pos++; // consume '('
                var value = ParseExpression();
                SkipWhitespace();
                if (_pos >= _input.Length || _input[_pos] != ')')
                    throw new InvalidOperationException("Missing closing parenthesis.");
                _pos++; // consume ')'
                return value;
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            SkipWhitespace();
            var start = _pos;
            while (_pos < _input.Length && (char.IsDigit(_input[_pos]) || _input[_pos] == '.'))
                _pos++;

            if (_pos == start)
                throw new InvalidOperationException(
                    $"Expected number at position {_pos}, got '{(_pos < _input.Length ? _input[_pos] : '?')}'.");

            var token = _input[start.._pos];
            if (!double.TryParse(token, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
                throw new InvalidOperationException($"Invalid number: '{token}'.");

            return number;
        }

        private void SkipWhitespace()
        {
            while (_pos < _input.Length && char.IsWhiteSpace(_input[_pos]))
                _pos++;
        }
    }
}

/// <summary>
/// Stub tool for web search. Requires an <see cref="IWebSearchProvider"/> to perform real searches.
/// Without one it returns a friendly configuration message.
/// </summary>
public sealed class WebSearchTool
{
    private readonly IWebSearchProvider? _provider;

    /// <summary>
    /// Creates a <see cref="WebSearchTool"/>.
    /// </summary>
    /// <param name="provider">
    /// An optional search provider. When <c>null</c> the tool returns a stub message instead of performing a search.
    /// </param>
    public WebSearchTool(IWebSearchProvider? provider = null) => _provider = provider;

    /// <summary>
    /// Searches the internet for current information. Returns results as plain text.
    /// If no <see cref="IWebSearchProvider"/> is configured, returns a stub message.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    [LLMTool("search_web", "Search the internet for current information")]
    public async Task<string> SearchAsync(
        [Description("Search query")] string query,
        [Description("Max results")] int maxResults = 5)
    {
        if (_provider is null)
            return "Web search not configured. Inject IWebSearchProvider.";

        return await _provider.SearchAsync(query, maxResults, CancellationToken.None)
            .ConfigureAwait(false);
    }
}
