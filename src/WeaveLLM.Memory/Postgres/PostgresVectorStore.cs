#nullable enable
using System.Text.Json;
using Npgsql;
using WeaveLLM.Core.Memory;
using WeaveLLM.Core.Models;

namespace WeaveLLM.Memory.Postgres;

/// <summary>
/// PostgreSQL-backed vector store using the <c>pgvector</c> extension for cosine-similarity search.
/// Call <see cref="InitialiseAsync"/> once at startup to create the table and index if they do not exist.
/// </summary>
public sealed class PostgresVectorStore : IVectorStore
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly int _dimensions;

    /// <summary>
    /// Initialises a new <see cref="PostgresVectorStore"/>.
    /// </summary>
    /// <param name="connectionString">Npgsql connection string targeting a PostgreSQL database with the <c>pgvector</c> extension available.</param>
    /// <param name="tableName">Name of the table used to store vectors. Defaults to <c>weavellm_vectors</c>.</param>
    /// <param name="dimensions">Embedding dimension. Must match the model used to generate vectors. Defaults to 1536 (OpenAI text-embedding-3-small).</param>
    public PostgresVectorStore(
        string connectionString,
        string tableName = "weavellm_vectors",
        int dimensions = 1536)
    {
        _connectionString = connectionString;
        _tableName = ValidateTableName(tableName);
        _dimensions = dimensions;
    }

    /// <summary>
    /// Creates the <c>vector</c> extension, table, and IVFFlat cosine index if they do not already exist.
    /// Safe to call on every startup — all statements use <c>IF NOT EXISTS</c>.
    /// Requires the <c>pgvector</c> extension to be installed on the PostgreSQL server.
    /// </summary>
    /// <param name="cancellationToken">Cancellation support.</param>
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            CREATE EXTENSION IF NOT EXISTS vector;
            CREATE TABLE IF NOT EXISTS {_tableName} (
                id         TEXT PRIMARY KEY,
                content    TEXT NOT NULL,
                embedding  vector({_dimensions}),
                metadata   JSONB,
                created_at TIMESTAMPTZ DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_{_tableName}_embedding
                ON {_tableName} USING ivfflat (embedding vector_cosine_ops);
            """;

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(VectorEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var vectorLiteral = FormatVector(entry.Vector);
            var metadataJson = entry.Metadata is null
                ? "{}"
                : JsonSerializer.Serialize(entry.Metadata);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_tableName} (id, content, embedding, metadata)
                VALUES ($1, $2, $3::vector, $4::jsonb)
                ON CONFLICT (id) DO UPDATE
                    SET content   = EXCLUDED.content,
                        embedding = EXCLUDED.embedding,
                        metadata  = EXCLUDED.metadata
                """;

            cmd.Parameters.AddWithValue(entry.Id);
            cmd.Parameters.AddWithValue(entry.Content);
            cmd.Parameters.AddWithValue(vectorLiteral);
            cmd.Parameters.AddWithValue(metadataJson);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                WeaveLLMError.ProviderError("postgres",
                    $"UpsertAsync failed. Verify the connection string and that InitialiseAsync has been called. Detail: {ex.Message}").Message,
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<ChainResult<IReadOnlyList<ScoredEntry>>> SearchAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var vectorLiteral = FormatVector(queryVector);

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT id, content, metadata,
                       1 - (embedding <=> $1::vector) AS score
                FROM {_tableName}
                ORDER BY embedding <=> $1::vector
                LIMIT $2
                """;

            cmd.Parameters.AddWithValue(vectorLiteral);
            cmd.Parameters.AddWithValue(topK);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var results = new List<ScoredEntry>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                var content = reader.GetString(1);
                var metadataJson = reader.IsDBNull(2) ? null : reader.GetString(2);
                var score = (float)reader.GetDouble(3);

                var metadata = ParseMetadata(metadataJson);
                results.Add(new ScoredEntry(new VectorEntry(id, [], content, metadata), score));
            }

            return ChainResult<IReadOnlyList<ScoredEntry>>.Success(results);
        }
        catch (NpgsqlException ex)
        {
            return ChainResult<IReadOnlyList<ScoredEntry>>.Failure(
                WeaveLLMError.ProviderError("postgres",
                    $"SearchAsync failed. Verify the connection string and that InitialiseAsync has been called. Detail: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"DELETE FROM {_tableName} WHERE id = $1";
            cmd.Parameters.AddWithValue(id);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                WeaveLLMError.ProviderError("postgres",
                    $"DeleteAsync failed. Verify the connection string and that InitialiseAsync has been called. Detail: {ex.Message}").Message,
                ex);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    private static string FormatVector(float[] vector) =>
        $"[{string.Join(",", vector)}]";

    private static IReadOnlyDictionary<string, string>? ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Guards against SQL injection via the table name configuration value.
    /// Only ASCII letters, digits, and underscores are permitted.
    /// </summary>
    private static string ValidateTableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^\w+$"))
            throw new ArgumentException(
                $"Table name '{name}' is invalid. Use only letters, digits, and underscores.", nameof(name));
        return name;
    }
}
