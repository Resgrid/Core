using System.Text.Json;
using Resgrid.Llm;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Ai;

public sealed record GroundedAskResult(string Outcome, IReadOnlyList<AskEvidence> Evidence, IReadOnlyList<AskToolInput> Reads, int InputTokens, int OutputTokens);

/// <summary>
/// What the model may know about the administrator's question. A department without Advanced Data Protection sends the
/// question itself (trimmed to <see cref="MaxQuestionLength"/>); an ADP department sends only the question words that also
/// occur in the reviewed public reference corpus (<see cref="IAdminAssistReferenceSearch.ReviewedTerms"/>), never both.
/// </summary>
public sealed record AskFocus(string? Question, IReadOnlyList<string>? Terms)
{
    public const int MaxQuestionLength = 1000;
    public const int MaxTerms = 24;
    public static AskFocus None { get; } = new(null, null);
    public static AskFocus FromQuestion(string question)
    {
        var text = string.Join(' ', (question ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return new(text.Length > MaxQuestionLength ? text[..MaxQuestionLength] : text, null);
    }
    public static AskFocus FromTerms(IEnumerable<string> terms) =>
        new(null, (terms ?? []).Where(t => !string.IsNullOrWhiteSpace(t) && t.Length <= 64).Distinct(StringComparer.Ordinal).Take(MaxTerms).ToArray());
}

/// <summary>
/// A bounded evidence selector. The model sees the <see cref="AskFocus"/> as untrusted data plus evidence cards; no history,
/// user grant, source writer or arbitrary network tool enters this runner, and output is limited to observed card ids.
/// </summary>
/// <param name="cacheSalt">The department's prefix-cache partition (<see cref="AiCacheIsolation"/>); null sends none.</param>
/// <param name="label">Resolves a card's reviewed catalog title key to its plain-language label for the model; null sends keys only.</param>
public sealed class GroundedAskRunner(ILlmClient client, IAdminAssistAskQueries queries, Func<CancellationToken, Task>? authorize = null, string? cacheSalt = null, Func<string, string>? label = null)
{
    public static readonly IReadOnlyDictionary<string, string[]> Modes = new Dictionary<string, string[]> {
        ["plans"] = ["draft_plan", "verify_step"],
        ["setup"] = ["get_setup_report", "get_setup_next_steps", "get_operating_profile", "get_findings"],
        ["settings"] = ["get_setting", "get_section", "evaluate_impact", "get_recent_changes"],
        ["permissions"] = ["get_permissions", "get_findings", "get_setting"],
        ["addons"] = ["search_capabilities", "explain_capability_access", "compare_addon_capabilities"],
        ["reference"] = ["search_reference", "search_docs", "get_section"]
    };
    /// <param name="focus">The question, or only its reviewed-corpus terms; null is <see cref="AskFocus.None"/>.</param>
    /// <param name="searchFallback">When the model abstains, return the seed reference-search cards as "Matched" (labelled as
    /// search matches, not a model selection) instead of nothing. Used with term-only focus, where the model sees less.</param>
    public async Task<GroundedAskResult> RunAsync(AdminAssistActor actor, string model, string mode, IReadOnlyList<AskToolInput> seedReads, int budget, CancellationToken ct,
        AskFocus? focus = null, bool searchFallback = false)
    {
        if (!Modes.TryGetValue(mode, out var names) || seedReads.Count > 3) throw new ArgumentException("Invalid topic.");
        var observed = new Dictionary<string, AskEvidence>(StringComparer.Ordinal);
        var reads = new List<AskToolInput>();
        async Task<IReadOnlyList<AskEvidence>> Read(AskToolInput input) {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var evidence = await queries.ReadAsync(actor, input, deadline.Token);
            if (evidence.Count > 8) throw new ArgumentException("Unbounded evidence.");
            reads.Add(input);
            foreach (var card in evidence) observed[card.Id] = card;
            return evidence;
        }
        int inputTokens = 0, outputTokens = 0, calls = seedReads.Count;
        static bool IsSearch(AskToolInput read) => read.Name is "search_reference" or "search_docs";
        // Fallback cards are re-read like any answer (or taken from the final re-read), so access or catalog changes since
        // the seed read still apply.
        async Task<GroundedAskResult> Abstain(IReadOnlyList<AskEvidence>? freshSearch = null) {
            if (searchFallback) {
                var matches = new Dictionary<string, AskEvidence>(StringComparer.Ordinal);
                if (freshSearch != null) foreach (var item in freshSearch) matches.TryAdd(item.Id, item);
                else foreach (var read in seedReads.Where(IsSearch).Distinct()) {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
                    foreach (var item in await queries.ReadAsync(actor, read, deadline.Token)) matches.TryAdd(item.Id, item);
                }
                if (matches.Count > 0) return new("Matched", matches.Values.Take(8).ToArray(), reads, inputTokens, outputTokens);
            }
            return new("Abstained", [], reads, inputTokens, outputTokens);
        }
        foreach (var seed in seedReads) await Read(seed);
        if (observed.Count == 0) return new("Abstained", [], reads, 0, 0);
        var offered = AdminAssistPrompt.Tools.Where(t => names.Contains(t.Name) && (t.Name is not ("evaluate_impact" or "verify_step") || seedReads.Any(s => s.Name == t.Name))).ToArray();
        var messages = new List<LlmMessage> { new("system", AdminAssistPrompt.System), new("user", FitOpening(model, focus ?? AskFocus.None, observed.Values.ToArray(), offered)) };
        // Four tool rounds, then one final selection with no tools. Each request has its own 6,144/2,048 cap.
        for (var round = 0; round <= 4; round++) {
            ct.ThrowIfCancellationRequested();
            var request = new LlmRequest(model, messages, round == 4 ? [] : offered, 512, cacheSalt);
            var upper = AdminAssistPrompt.InputBudget(request);
            if (upper > 6144 || inputTokens + outputTokens + upper + request.MaxOutputTokens > budget)
                return await Abstain();
            if (authorize != null) await authorize(ct);
            var result = await client.CompleteAsync(request, ct);
            inputTokens += result.InputTokens; outputTokens += result.OutputTokens;
            if (inputTokens + outputTokens > budget) throw new LlmUnavailableException();
            if (result.ToolCalls.Count == 0) {
                var selected = AdminAssistPrompt.ValidateAnswer(result.Content, observed);
                // Re-query before rendering: changed/revoked evidence is never replayed from a model's earlier observation.
                var current = new Dictionary<string, AskEvidence>(StringComparer.Ordinal);
                var freshSearch = new List<AskEvidence>();
                foreach (var read in reads.Distinct()) {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
                    foreach (var item in await queries.ReadAsync(actor, read, deadline.Token)) {
                        current[item.Id] = item;
                        if (IsSearch(read) && seedReads.Contains(read)) freshSearch.Add(item);
                    }
                }
                var fresh = selected.Where(e => current.TryGetValue(e.Id, out var item) && Equivalent(e, item)).Select(e => current[e.Id]).ToArray();
                return fresh.Length == 0 ? await Abstain(freshSearch) : new("Answered", fresh, reads, inputTokens, outputTokens);
            }
            if (round == 4 || (calls += result.ToolCalls.Count) > 8) throw new ArgumentException("Tool budget exceeded.");
            messages.Add(new("assistant", null, result.ToolCalls));
            foreach (var call in result.ToolCalls) {
                if (!offered.Any(t => t.Name == call.Name)) throw new ArgumentException("Tool was not offered.");
                var input = AdminAssistPrompt.ValidateCall(call);
                if (input.Name is "evaluate_impact" or "verify_step" && !seedReads.Any(s => s.Name == input.Name && s.Id == input.Id && s.Value == input.Value)) throw new ArgumentException("Impact proposal requires explicit user context.");
                var evidence = await Read(input);
                messages.Add(new("tool", Compact(evidence), ToolCallId: call.Id));
            }
        }
        throw new LlmUnavailableException();
    }
    // The opening turn: the focus (question or reviewed terms, untrusted data) and the seed cards.
    private string Opening(AskFocus focus, IEnumerable<AskEvidence> cards, bool labels = true) => focus.Question != null
        ? JsonSerializer.Serialize(new { question = focus.Question, cards = Project(cards, labels) }, Json)
        : focus.Terms is { Count: > 0 } ? JsonSerializer.Serialize(new { questionTerms = focus.Terms, cards = Project(cards, labels) }, Json)
        : JsonSerializer.Serialize(new { cards = Project(cards, labels) }, Json);
    // A long non-Latin question with labelled cards can pass the 6,144 cap. Shorten the question (never below 200
    // characters), then drop the labels, rather than abstain; the round's own budget check still has the last word.
    private string FitOpening(string model, AskFocus focus, IReadOnlyCollection<AskEvidence> cards, IReadOnlyList<LlmTool> offered)
    {
        bool Fits(string content) => AdminAssistPrompt.InputBudget(new LlmRequest(model, [new("system", AdminAssistPrompt.System), new("user", content)], offered, 512, cacheSalt)) <= 6144;
        var opening = Opening(focus, cards);
        while (!Fits(opening) && focus.Question is { Length: > 200 } question) {
            focus = focus with { Question = question[..Math.Max(200, question.Length * 3 / 4)] };
            opening = Opening(focus, cards);
        }
        return Fits(opening) ? opening : Opening(focus, cards, labels: false);
    }
    // Cards carry only catalog keys and reviewed labels, scalar facts and state codes. No source URL, user ID, card body or historical answer.
    private string Compact(IEnumerable<AskEvidence> cards) => JsonSerializer.Serialize(Project(cards), Json);
    private object[] Project(IEnumerable<AskEvidence> cards, bool labels = true) => cards.Take(8).Select(e => (object)new { id = e.Id, kind = e.Kind, title = e.TitleKey, label = labels ? Label(e.TitleKey) : null, state = e.State, numbers = e.Numbers }).ToArray();
    private string? Label(string key) {
        if (label == null || string.IsNullOrEmpty(key)) return null;
        var text = label(key);
        return string.IsNullOrWhiteSpace(text) || text == key ? null : text.Length > 120 ? text[..120] : text;
    }
    // Relaxed escaping keeps non-Latin questions and labels as UTF-8 instead of six-byte \uXXXX escapes, so a Greek or
    // Arabic question does not blow the input budget. The content is a JSON string inside a JSON chat body either way.
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    public static bool Equivalent(AskEvidence before, AskEvidence after) =>
        before.CatalogVersion == after.CatalogVersion && before.SnapshotRevision == after.SnapshotRevision && before.State == after.State &&
        before.Destination == after.Destination && before.TitleKey == after.TitleKey && before.TextKeys.SequenceEqual(after.TextKeys) &&
        before.Numbers.Count == after.Numbers.Count && before.Numbers.All(p => after.Numbers.TryGetValue(p.Key, out var value) && value == p.Value) && before.PublicText == after.PublicText;
}
