using System.Text.Json;

namespace Resgrid.Llm;

public sealed record LlmTool(string Name, string Description, JsonElement Parameters);
public sealed record LlmToolCall(string Id, string Name, string Arguments);
public sealed record LlmMessage(string Role, string? Content, IReadOnlyList<LlmToolCall>? ToolCalls = null, string? ToolCallId = null);
public sealed record LlmRequest(string Model, IReadOnlyList<LlmMessage> Messages, IReadOnlyList<LlmTool> Tools, int MaxOutputTokens = 512);
public sealed record LlmResult(string? Content, IReadOnlyList<LlmToolCall> ToolCalls, int InputTokens, int OutputTokens, string FinishReason);
public interface ILlmClient
{
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
public sealed class LlmUnavailableException : Exception
{
    public LlmUnavailableException() : base("Inference is unavailable.") { }
}
