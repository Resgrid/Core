namespace Resgrid.Llm;

/// <summary>Reads a successful inference response body without ever buffering more than <c>maxBytes</c>.</summary>
internal static class BoundedResponse
{
    public static async Task<byte[]> ReadAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes) throw new LlmUnavailableException();
        using var source = await response.Content.ReadAsStreamAsync(ct);
        using var bounded = new MemoryStream(); var buffer = new byte[4096]; int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0) {
            if (bounded.Length + read > maxBytes) throw new LlmUnavailableException();
            bounded.Write(buffer, 0, read);
        }
        return bounded.ToArray();
    }
}
