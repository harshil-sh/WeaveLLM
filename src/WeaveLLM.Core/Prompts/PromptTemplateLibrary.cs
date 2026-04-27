namespace WeaveLLM.Core.Prompts;

/// <summary>
/// Pre-built prompt templates for common WeaveLLM workflows.
/// </summary>
public static class PromptTemplateLibrary
{
    /// <summary>ReAct agent system prompt. Requires: {{tools}}.</summary>
    public static readonly IPromptTemplate ReActSystemPrompt = PromptTemplate.Create(
        "react_system_prompt",
        """
        You are an AI assistant. Use tools to answer questions.
        Available tools: {{tools}}
        Format: Thought: ... Action: tool_name Action Input: {...} Observation: ...
        """);

    /// <summary>RAG query prompt. Requires: {{context}}, {{question}}.</summary>
    public static readonly IPromptTemplate RagQueryPrompt = PromptTemplate.Create(
        "rag_query_prompt",
        """
        Answer using ONLY the context below. If unsure, say so.
        Context: {{context}}
        Question: {{question}}
        """);

    /// <summary>Summarisation prompt. Requires: {{text}}. Optional: {{tone}} (defaults to "neutral" if omitted).</summary>
    public static readonly IPromptTemplate SummarisationPrompt = PromptTemplate.Create(
        "summarisation_prompt",
        "Summarise the following in a {{tone}} tone:\n{{text}}",
        optionalVars: ["tone"]);

    /// <summary>Critique prompt. Requires: {{draft}}.</summary>
    public static readonly IPromptTemplate CritiquePrompt = PromptTemplate.Create(
        "critique_prompt",
        """
        Review the following draft and list specific improvements:
        Draft: {{draft}}
        """);
}
