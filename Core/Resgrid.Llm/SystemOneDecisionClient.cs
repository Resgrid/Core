using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Resgrid.Llm;

/// <summary>
/// Decision-model transport for Cloudflare Clef / Clef-Flash: Workers AI REST (<paramref name="workersAi"/>, answers inside
/// Cloudflare's <c>{"result": …, "success": true}</c>) or an operator SGLang server at /v1/systemone (bare answers). Workers AI is a
/// third party, so it is sent only <see cref="DecisionDataClass.NonSensitive"/> and <see cref="DecisionDataClass.DepartmentConfiguration"/>
/// requests: department content (anything that may hold PII or PHI) is refused before a byte leaves. Same rules as OpenAiToolClient (no retries, redirects, payload logs, provider
/// fallback or network tools; bounded request and response; every failure is LlmUnavailableException with nothing from the payload
/// in it). Only the probabilities of the options asked are read back, and they must cover exactly those options; anything else is
/// a failure, never a repair.
/// </summary>
public sealed class SystemOneDecisionClient(HttpClient client, Uri endpoint, string apiKey, bool workersAi = false) : IDecisionClient, IDisposable
{
    public const int MaxQuestions = 64;
    public const int MaxOptions = 64;
    public const int MaxBytes = 65536;
    private static readonly Regex IdPattern = new("\\A[A-Za-z0-9_.-]{1,100}\\z", RegexOptions.CultureInvariant);

    public void Dispose() => client.Dispose();

    public async Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new LlmUnavailableException();
        // Fail closed: a third-party host never receives department content, whatever the caller configured.
        if (workersAi && request?.DataClass is not (DecisionDataClass.NonSensitive or DecisionDataClass.DepartmentConfiguration)) throw new LlmUnavailableException();
        var body = Body(request!);
        using var outgoing = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        outgoing.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        outgoing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await client.SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead, ct);
            return Read(request!, await BoundedResponse.ReadAsync(response, MaxBytes, ct), body.Length, workersAi);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { throw new LlmUnavailableException(); } // No response bodies/credentials in exceptions or telemetry.
    }

    /// <summary>The wire body. Throws <see cref="LlmUnavailableException"/> for a request the server would refuse or that is out of bounds.</summary>
    public static byte[] Body(DecisionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Model) || request.Model.Length > 128 || request.State == null || request.Questions is not { Count: >= 1 and <= MaxQuestions } ||
            request.Questions.Any(q => q == null) || request.Questions.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count() != request.Questions.Count)
            throw new LlmUnavailableException();
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("model", request.Model);
            json.WriteString("state", request.State);
            json.WriteStartObject("questions");
            foreach (var question in request.Questions)
            {
                var options = question.Options ?? [];
                if (question.Id == null || !IdPattern.IsMatch(question.Id) || string.IsNullOrWhiteSpace(question.Instructions) || question.Instructions.Length > 1000 ||
                    options.Any(o => o == null || o.Id == null || !IdPattern.IsMatch(o.Id) || string.IsNullOrWhiteSpace(o.Description) || o.Description.Length > 500) ||
                    options.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != options.Count)
                    throw new LlmUnavailableException();
                json.WriteStartObject(question.Id);
                json.WriteString("instructions", question.Instructions);
                switch (question.Type)
                {
                    case DecisionQuestionType.Noul:
                        json.WriteString("type", "noul");
                        if (options.Count == 0) break;
                        if (options.Count != 2 || !options.Any(o => o.Id == "true") || !options.Any(o => o.Id == "false")) throw new LlmUnavailableException();
                        json.WriteStartObject("criteria");
                        foreach (var option in options) json.WriteString(option.Id, option.Description);
                        json.WriteEndObject();
                        break;
                    case DecisionQuestionType.Choice:
                        json.WriteString("type", "choice");
                        if (options.Count is < 2 or > MaxOptions) throw new LlmUnavailableException();
                        json.WriteStartObject("criteria");
                        foreach (var option in options) json.WriteString(option.Id, option.Description);
                        json.WriteEndObject();
                        break;
                    case DecisionQuestionType.Score:
                        json.WriteString("type", "score");
                        if (options.Count is < 2 or > 10 || options.Where((o, i) => o.Id != i.ToString(CultureInfo.InvariantCulture)).Any()) throw new LlmUnavailableException();
                        json.WriteStartArray("criteria");
                        foreach (var option in options) json.WriteStringValue(option.Description);
                        json.WriteEndArray();
                        break;
                    default:
                        throw new LlmUnavailableException();
                }
                json.WriteEndObject();
            }
            json.WriteEndObject();
            json.WriteEndObject();
        }
        if (buffer.Length > MaxBytes) throw new LlmUnavailableException();
        return buffer.ToArray();
    }

    /// <summary>
    /// Reads the SystemOne answers for exactly the questions asked. The most probable option is recomputed from the probabilities
    /// (ties go to the earlier option the caller listed); the server's own <c>confidence</c> formula is not relied on.
    /// </summary>
    public static DecisionResult Read(DecisionRequest request, byte[] body, int requestBytes, bool cloudflareEnvelope = false)
    {
        using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 9 });
        var root = document.RootElement;
        if (cloudflareEnvelope)
        {
            if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True) throw new LlmUnavailableException();
            root = root.GetProperty("result");
            if (root.ValueKind != JsonValueKind.Object) throw new LlmUnavailableException();
        }
        var answers = root.GetProperty("answers");
        if (answers.ValueKind != JsonValueKind.Object || answers.EnumerateObject().Count() != request.Questions.Count) throw new LlmUnavailableException();
        var result = new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal);
        foreach (var question in request.Questions)
        {
            var answer = answers.GetProperty(question.Id);
            if (answer.ValueKind != JsonValueKind.Object) throw new LlmUnavailableException();
            result[question.Id] = question.Type == DecisionQuestionType.Noul ? Noul(answer) : Ranked(question, answer);
        }
        return new(result, InputTokens(root, requestBytes));
    }

    private static DecisionAnswer Noul(JsonElement answer)
    {
        var p = answer.TryGetProperty("noul", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble()
            : answer.TryGetProperty("probabilities", out var probabilities) && probabilities.ValueKind == JsonValueKind.Object &&
              probabilities.TryGetProperty("true", out var yes) && yes.ValueKind == JsonValueKind.Number ? yes.GetDouble() : double.NaN;
        if (!(p is >= 0 and <= 1)) throw new LlmUnavailableException();
        return new(p >= 0.5 ? "true" : "false", Math.Max(p, 1 - p), new Dictionary<string, double>(StringComparer.Ordinal) { ["true"] = p, ["false"] = 1 - p });
    }

    private static DecisionAnswer Ranked(DecisionQuestion question, JsonElement answer)
    {
        var options = question.Options!;
        var probabilities = answer.GetProperty("probabilities");
        if (probabilities.ValueKind != JsonValueKind.Object || probabilities.EnumerateObject().Count() != options.Count) throw new LlmUnavailableException();
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var option in options)
        {
            var value = probabilities.GetProperty(option.Id);
            if (value.ValueKind != JsonValueKind.Number || value.GetDouble() is not (>= 0 and <= 1)) throw new LlmUnavailableException();
            values[option.Id] = value.GetDouble();
        }
        // The server rounds each probability to four places; a sum far from one is not a softmax.
        if (Math.Abs(values.Values.Sum() - 1) > 0.02) throw new LlmUnavailableException();
        var best = options[0].Id;
        foreach (var option in options) if (values[option.Id] > values[best]) best = option.Id;
        double? score = question.Type == DecisionQuestionType.Score ? options.Select((o, i) => i * values[o.Id]).Sum() : null;
        return new(best, values[best], values, score);
    }

    // Servers report input_tokens (reference) or prompt_tokens (OpenAI style). Without usage, a byte count over three is a safe upper estimate.
    private static int InputTokens(JsonElement root, int requestBytes)
    {
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            foreach (var name in new[] { "input_tokens", "prompt_tokens" })
                if (usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var tokens))
                    return tokens is >= 0 and <= 32768 ? tokens : throw new LlmUnavailableException();
        return Math.Max(1, requestBytes / 3);
    }
}
