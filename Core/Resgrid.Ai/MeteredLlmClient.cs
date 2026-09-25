using Resgrid.Llm;

namespace Resgrid.Ai;

/// <summary>Separates verified provider usage from requests whose completion is unknown.</summary>
public sealed class MeteredLlmClient(ILlmClient inner) : ILlmClient
{
    public bool HasUnsettledRequest { get; private set; }
    public int VerifiedTokens { get; private set; }
    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        HasUnsettledRequest = true;
        var result = await inner.CompleteAsync(request, ct);
        VerifiedTokens = checked(VerifiedTokens + result.InputTokens + result.OutputTokens);
        HasUnsettledRequest = false;
        return result;
    }
}
