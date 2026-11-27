using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Resgrid.Llm;

/// <summary>
/// Transport for text-embeddings-inference's <c>POST /embed</c> (<c>{"inputs":[...],"normalize":true,"truncate":true}</c> →
/// <c>[[float,...],...]</c>). The endpoint is the operator's (exact <see cref="OperatorEndpointPolicy.EmbedPath"/>, private only when the
/// operator allows it), every call is bounded by the caller's token, the response is read with a byte limit, and any shape other than one
/// finite vector of <paramref name="dimensions"/> per input is refused. Failures surface as <see cref="LlmUnavailableException"/>.
/// </summary>
public sealed class TeiEmbeddingClient(HttpClient client, Uri endpoint, string? apiKey, int dimensions)
{
    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        if (inputs == null || inputs.Count == 0) return Array.Empty<float[]>();
        if (dimensions <= 0 || dimensions > 8192) throw new LlmUnavailableException();
        var body = JsonSerializer.Serialize(new { inputs, normalize = true, truncate = true });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        // A float printed as JSON is at most ~25 bytes; the limit leaves room for that and the brackets.
        var maxBytes = checked(inputs.Count * dimensions * 28 + 4096);
        using var document = JsonDocument.Parse(await BoundedResponse.ReadAsync(response, maxBytes, ct), new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != inputs.Count) throw new LlmUnavailableException();
        var vectors = new List<float[]>(inputs.Count);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != dimensions) throw new LlmUnavailableException();
            var vector = new float[dimensions];
            var i = 0;
            foreach (var value in row.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number) || !float.IsFinite(number)) throw new LlmUnavailableException();
                vector[i++] = number;
            }
            vectors.Add(vector);
        }
        return vectors;
    }
}
