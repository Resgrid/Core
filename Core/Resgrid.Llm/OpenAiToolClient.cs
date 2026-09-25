using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Resgrid.Llm;

/// <summary>Typed compatible transport with no retries, redirects, payload logs, provider fallback or network tools.</summary>
public sealed class OpenAiToolClient(HttpClient client, Uri endpoint, string apiKey) : ILlmClient, IDisposable
{
    public void Dispose() => client.Dispose();

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || request.MaxOutputTokens is < 1 or > 2048 || request.Messages.Count is < 2 or > 32 || request.Tools.Count > 16) throw new LlmUnavailableException();
        var messages = request.Messages.Select(m => {
            var value = new Dictionary<string, object?> { ["role"] = m.Role, ["content"] = m.Content };
            if (m.ToolCallId != null) value["tool_call_id"] = m.ToolCallId;
            if (m.ToolCalls?.Count > 0) value["tool_calls"] = m.ToolCalls.Select(t => new { id = t.Id, type = "function", function = new { name = t.Name, arguments = t.Arguments } }).ToArray();
            return value;
        }).ToArray();
        var body = JsonSerializer.Serialize(new { model = request.Model, messages, max_tokens = request.MaxOutputTokens, temperature = 0,
            tools = request.Tools.Select(t => new { type = "function", function = new { name = t.Name, description = t.Description, parameters = t.Parameters } }).ToArray(),
            tool_choice = request.Tools.Count > 0 ? "auto" : "none", parallel_tool_calls = false, stream = false, chat_template_kwargs = new { enable_thinking = false } });
        if (Encoding.UTF8.GetByteCount(body) > 65536) throw new LlmUnavailableException();
        using var outgoing = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        outgoing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await client.SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 65536) throw new LlmUnavailableException();
            using var source = await response.Content.ReadAsStreamAsync(ct);
            using var bounded = new MemoryStream(); var buffer = new byte[4096]; int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0) {
                if (bounded.Length + read > 65536) throw new LlmUnavailableException();
                bounded.Write(buffer, 0, read);
            }
            using var document = JsonDocument.Parse(bounded.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            var choices = root.GetProperty("choices"); if (choices.GetArrayLength() != 1) throw new LlmUnavailableException();
            var choice = choices[0]; var message = choice.GetProperty("message");
            var finish = choice.GetProperty("finish_reason").GetString(); if (finish is not ("stop" or "tool_calls")) throw new LlmUnavailableException();
            var calls = new List<LlmToolCall>();
            if (message.TryGetProperty("tool_calls", out var tools) && tools.ValueKind == JsonValueKind.Array) foreach (var tool in tools.EnumerateArray()) {
                if (calls.Count >= 8 || tool.GetProperty("type").GetString() != "function") throw new LlmUnavailableException();
                var function = tool.GetProperty("function");
                calls.Add(new(tool.GetProperty("id").GetString() ?? "", function.GetProperty("name").GetString() ?? "", function.GetProperty("arguments").GetString() ?? ""));
            }
            var usage = root.GetProperty("usage"); var input = usage.GetProperty("prompt_tokens").GetInt32(); var output = usage.GetProperty("completion_tokens").GetInt32();
            if (input < 0 || input > 6144 || output < 0 || output > request.MaxOutputTokens || calls.Any(c => c.Id.Length is < 1 or > 128 || c.Arguments.Length > 4096 || c.Name.Length > 64) || calls.Select(c => c.Id).Distinct().Count() != calls.Count) throw new LlmUnavailableException();
            return new(message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : null, calls, input, output, finish!);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { throw new LlmUnavailableException(); } // No response bodies/credentials in exceptions or telemetry.
    }
}
