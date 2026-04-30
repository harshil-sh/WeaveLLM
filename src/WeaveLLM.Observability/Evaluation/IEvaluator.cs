namespace WeaveLLM.Observability.Evaluation;

/// <summary>
/// The input supplied to an evaluator for a single question/answer pair.
/// </summary>
/// <param name="Question">The question posed to the model.</param>
/// <param name="Answer">The model's answer to be evaluated.</param>
/// <param name="Context">Optional grounding context (e.g., retrieved documents) used to generate the answer.</param>
public sealed record EvaluationInput(string Question, string Answer, string? Context);

/// <summary>
/// The scored output produced by an <see cref="IEvaluator"/> for one <see cref="EvaluationInput"/>.
/// </summary>
/// <param name="Metric">The name of the metric being measured (e.g., <c>"faithfulness"</c>, <c>"relevance"</c>).</param>
/// <param name="Score">A normalised score in the range [0.0, 1.0], where 1.0 is best.</param>
/// <param name="Reasoning">A brief natural-language explanation of why this score was assigned.</param>
public sealed record EvaluationResult(string Metric, float Score, string Reasoning);

/// <summary>
/// Evaluates a model's answer on a single quality metric.
/// </summary>
public interface IEvaluator
{
    /// <summary>
    /// Scores the answer contained in <paramref name="input"/> and returns a structured <see cref="EvaluationResult"/>.
    /// </summary>
    /// <param name="input">The question, answer, and optional context to evaluate.</param>
    /// <param name="cancellationToken">Token to cancel the evaluation call.</param>
    Task<EvaluationResult> EvaluateAsync(EvaluationInput input, CancellationToken cancellationToken = default);
}
