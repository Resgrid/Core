using System.Text.Json;
using Resgrid.Llm;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Ai;

public sealed record GroundedAskResult(string Outcome, IReadOnlyList<AskEvidence> Evidence, IReadOnlyList<AskToolInput> Reads, int InputTokens, int OutputTokens);

/// <summary>A bounded evidence selector. No raw question/history, user grant, source writer or arbitrary network tool enters this runner.</summary>
public sealed class GroundedAskRunner(ILlmClient client, IAdminAssistAskQueries queries, Func<CancellationToken, Task>? authorize = null)
{
    public static readonly IReadOnlyDictionary<string, string[]> Modes = new Dictionary<string, string[]> {
        ["setup"] = ["get_setup_report", "get_setup_next_steps", "get_operating_profile", "get_findings"],
        ["settings"] = ["get_setting", "get_section", "evaluate_impact", "get_recent_changes"],
        ["permissions"] = ["get_permissions", "get_findings", "get_setting"],
        ["addons"] = ["search_capabilities", "explain_capability_access", "compare_addon_capabilities"],
        ["reference"] = ["search_reference", "search_docs", "get_section"]
    };
    public async Task<GroundedAskResult> RunAsync(AdminAssistActor actor, string model, string mode, IReadOnlyList<AskToolInput> seedReads, int budget, CancellationToken ct)
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
        foreach (var seed in seedReads) await Read(seed);
        if (observed.Count == 0) return new("Abstained", [], reads, 0, 0);
        var offered = AdminAssistPrompt.Tools.Where(t => names.Contains(t.Name) && (t.Name != "evaluate_impact" || seedReads.Any(s => s.Name == "evaluate_impact"))).ToArray();
        var messages = new List<LlmMessage> { new("system", AdminAssistPrompt.System), new("user", Compact(observed.Values)) };
        int inputTokens = 0, outputTokens = 0, calls = seedReads.Count;
        // Four tool rounds, then one final selection with no tools. Each request has its own 6,144/2,048 cap.
        for (var round = 0; round <= 4; round++) {
            ct.ThrowIfCancellationRequested();
            var request = new LlmRequest(model, messages, round == 4 ? [] : offered, 512);
            var upper = AdminAssistPrompt.InputBudget(request);
            if (upper > 6144 || inputTokens + outputTokens + upper + request.MaxOutputTokens > budget)
                return new("Abstained", [], reads, inputTokens, outputTokens);
            if (authorize != null) await authorize(ct);
            var result = await client.CompleteAsync(request, ct);
            inputTokens += result.InputTokens; outputTokens += result.OutputTokens;
            if (inputTokens + outputTokens > budget) throw new LlmUnavailableException();
            if (result.ToolCalls.Count == 0) {
                var selected = AdminAssistPrompt.ValidateAnswer(result.Content, observed);
                // Re-query before rendering: changed/revoked evidence is never replayed from a model's earlier observation.
                var current = new Dictionary<string, AskEvidence>(StringComparer.Ordinal);
                foreach (var read in reads.Distinct()) {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(20));
                    foreach (var item in await queries.ReadAsync(actor, read, deadline.Token)) current[item.Id] = item;
                }
                var fresh = selected.Where(e => current.TryGetValue(e.Id, out var item) && Equivalent(e, item)).Select(e => current[e.Id]).ToArray();
                return new(fresh.Length == 0 ? "Abstained" : "Answered", fresh, reads, inputTokens, outputTokens);
            }
            if (round == 4 || (calls += result.ToolCalls.Count) > 8) throw new ArgumentException("Tool budget exceeded.");
            messages.Add(new("assistant", null, result.ToolCalls));
            foreach (var call in result.ToolCalls) {
                if (!offered.Any(t => t.Name == call.Name)) throw new ArgumentException("Tool was not offered.");
                var input = AdminAssistPrompt.ValidateCall(call);
                if (input.Name == "evaluate_impact" && !seedReads.Any(s => s.Name == input.Name && s.Id == input.Id && s.Value == input.Value)) throw new ArgumentException("Impact proposal requires explicit user context.");
                var evidence = await Read(input);
                messages.Add(new("tool", Compact(evidence), ToolCallId: call.Id));
            }
        }
        throw new LlmUnavailableException();
    }
    // Only catalog labels, scalar facts and state codes. No question, source URL, user ID, free text or historical answer.
    private static string Compact(IEnumerable<AskEvidence> cards) => JsonSerializer.Serialize(cards.Take(8).Select(e => new { id = e.Id, kind = e.Kind, title = e.TitleKey, state = e.State, numbers = e.Numbers }));
    public static bool Equivalent(AskEvidence before, AskEvidence after) =>
        before.CatalogVersion == after.CatalogVersion && before.SnapshotRevision == after.SnapshotRevision && before.State == after.State &&
        before.Destination == after.Destination && before.TitleKey == after.TitleKey && before.TextKeys.SequenceEqual(after.TextKeys) &&
        before.Numbers.Count == after.Numbers.Count && before.Numbers.All(p => after.Numbers.TryGetValue(p.Key, out var value) && value == p.Value) && before.PublicText == after.PublicText;
}
