namespace WeaveLLM.Observability.Evaluation;

/// <summary>
/// An aggregated report produced by <see cref="EvaluationSuite.RunAsync"/>.
/// </summary>
public sealed record EvaluationReport
{
    /// <summary>
    /// Per-metric average scores across all evaluated inputs.
    /// Keys are metric names (e.g., <c>"faithfulness"</c>); values are mean scores in [0.0, 1.0].
    /// </summary>
    public required Dictionary<string, float> AverageScores { get; init; }

    /// <summary>
    /// Every individual <see cref="EvaluationResult"/> produced during the run,
    /// in the order evaluators × inputs were processed.
    /// </summary>
    public required IReadOnlyList<EvaluationResult> AllResults { get; init; }
}

/// <summary>
/// Runs a collection of <see cref="IEvaluator"/> instances over a set of <see cref="EvaluationInput"/> samples
/// and aggregates the results into an <see cref="EvaluationReport"/>.
/// </summary>
public sealed class EvaluationSuite
{
    private readonly IReadOnlyList<IEvaluator> _evaluators;

    /// <summary>
    /// Initialises a new <see cref="EvaluationSuite"/> with the specified evaluators.
    /// </summary>
    /// <param name="evaluators">The evaluators to run over each input sample.</param>
    public EvaluationSuite(IReadOnlyList<IEvaluator> evaluators)
    {
        _evaluators = evaluators;
    }

    /// <summary>
    /// Evaluates every input with every evaluator and returns an aggregated <see cref="EvaluationReport"/>.
    /// Each evaluator is applied to each input sequentially to avoid overwhelming the upstream model.
    /// </summary>
    /// <param name="inputs">The question/answer samples to evaluate.</param>
    /// <param name="cancellationToken">Token to cancel the run.</param>
    public async Task<EvaluationReport> RunAsync(
        IReadOnlyList<EvaluationInput> inputs,
        CancellationToken cancellationToken = default)
    {
        var allResults = new List<EvaluationResult>();

        foreach (var evaluator in _evaluators)
        {
            foreach (var input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await evaluator.EvaluateAsync(input, cancellationToken);
                allResults.Add(result);
            }
        }

        var averageScores = allResults
            .GroupBy(r => r.Metric)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.Score).Average());

        return new EvaluationReport
        {
            AverageScores = averageScores,
            AllResults = allResults.AsReadOnly()
        };
    }
}
