#nullable enable
namespace WeaveLLM.Core.Providers
{
    /// <summary>
    /// Compatibility shim — <see cref="IEmbeddingModel"/> has moved to
    /// <see cref="WeaveLLM.Core.Providers.Embeddings.IEmbeddingModel"/>.
    /// This alias will be removed in 0.3.0.
    /// </summary>
    [Obsolete("IEmbeddingModel has moved to WeaveLLM.Core.Providers.Embeddings. " +
              "Update your using directive. This shim will be removed in 0.3.0.",
              error: false)]
    public interface IEmbeddingModel : Embeddings.IEmbeddingModel { }
}
