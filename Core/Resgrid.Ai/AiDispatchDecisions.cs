using System.Globalization;
using Resgrid.Llm;
using Resgrid.Model.AiDispatch;

namespace Resgrid.Ai;

/// <summary>The decision model's answers, mapped back onto the department's own lists. A null value is "none" or nothing offered.</summary>
public sealed record AiDispatchDecision(double DispatchProbability, AiDispatchChoice? CallType, double CallTypeProbability, AiDispatchChoice? Priority,
    double PriorityProbability, AiDispatchCandidateCall? RelatedCall, double RelatedCallProbability, int InputTokens);

/// <summary>
/// The classification half of Enrich mode on a decision model (Cloudflare Clef / Clef-Flash; ai-dispatch-template-plan.md §4).
/// Whether the message is a dispatch, which of the department's call types and priorities fits, and whether it repeats an active
/// call are each a probability over the department's own allowlisted options plus "none", scored in one forward pass: nothing
/// outside the lists can come back, and each field is used only when its own probability clears the department's confidence floor.
/// The generative model keeps verbatim extraction, title and summary. The state is the one call's message; nothing from another
/// department is ever in it. Call messages and active calls carry PII and often PHI, so every request is
/// <see cref="DecisionDataClass.DepartmentContent"/>: it is scored only by a self-hosted model, never by Workers AI.
/// </summary>
public static class AiDispatchDecisions
{
    public const string Version = "ai-dispatch-decide-v1";
    public const string None = "none";
    public const string IsDispatchQuestion = "is_dispatch";
    public const string CallTypeQuestion = "call_type";
    public const string PriorityQuestion = "priority";
    public const string DuplicateQuestion = "duplicate";

    public static DecisionRequest Request(string model, AiDispatchContext context)
    {
        var questions = new List<DecisionQuestion>
        {
            new(IsDispatchQuestion, DecisionQuestionType.Noul,
                "Is this message a real request for the department to respond to an incident, rather than an automated reply, out-of-office notice, test page, heartbeat or administrative message?")
        };
        // Option ids are prefixed list ids; "none" is listed first so a tie goes to it and never invents a classification.
        if (context.CallTypes.Count > 0)
            questions.Add(new(CallTypeQuestion, DecisionQuestionType.Choice,
                "Which of the department's call types best describes the incident in the message? Choose none when no listed type clearly fits.",
                Options(context.CallTypes.Select(c => new DecisionOption(Key("t", c.Id), Label(c.Name))), "No listed call type clearly fits the message.")));
        if (context.Priorities.Count > 0)
            questions.Add(new(PriorityQuestion, DecisionQuestionType.Choice,
                "Which of the department's call priorities best fits the urgency the message describes? Choose none when the message does not indicate it.",
                Options(context.Priorities.Select(p => new DecisionOption(Key("p", p.Id), Label(p.Name))), "The message does not indicate a priority.")));
        if (context.Candidates.Count > 0)
            questions.Add(new(DuplicateQuestion, DecisionQuestionType.Choice,
                "Does the message describe the same incident as one of these active calls? Choose none unless it clearly does.",
                Options(context.Candidates.Select(c => new DecisionOption(Key("c", c.CallId), Label(Candidate(c)))), "The message is not about any listed active call.")));
        return new(model, context.Message ?? "", questions, DecisionDataClass.DepartmentContent);
    }

    /// <summary>Maps answers back onto the context lists. Throws <see cref="ArgumentException"/> when an expected answer is missing.</summary>
    public static AiDispatchDecision Read(DecisionResult result, AiDispatchContext context)
    {
        if (result?.Answers == null || !result.Answers.TryGetValue(IsDispatchQuestion, out var dispatch)) throw new ArgumentException("No decision.");
        (T? Value, double Probability) Pick<T>(string question, IReadOnlyList<T> items, string prefix, Func<T, int> id) where T : class
        {
            if (items.Count == 0) return (null, 0);
            if (!result.Answers.TryGetValue(question, out var answer)) throw new ArgumentException("No decision.");
            if (answer.Choice == None) return (null, answer.Confidence);
            var match = items.FirstOrDefault(i => Key(prefix, id(i)) == answer.Choice) ?? throw new ArgumentException("Decision outside the list.");
            return (match, answer.Confidence);
        }
        var type = Pick(CallTypeQuestion, context.CallTypes, "t", c => c.Id);
        var priority = Pick(PriorityQuestion, context.Priorities, "p", p => p.Id);
        var related = Pick(DuplicateQuestion, context.Candidates, "c", c => c.CallId);
        return new(dispatch.Probability("true"), type.Value, type.Probability, priority.Value, priority.Probability, related.Value, related.Probability, result.InputTokens);
    }

    /// <summary>
    /// The decision model replaces the generative model's classification fields and its self-reported confidence; the result's
    /// confidence is how sure the decision model is about dispatch-or-not, which every fill depends on. Generated text is kept only
    /// when the generative model itself cleared the floor. <paramref name="generated"/> is null when it was skipped or failed.
    /// </summary>
    public static AiDispatchEnrichment Merge(AiDispatchEnrichment? generated, AiDispatchDecision decision, double minimumConfidence)
    {
        T? Accept<T>(T? value, double probability) where T : class => value != null && probability >= minimumConfidence ? value : null;
        var text = generated != null && generated.Confidence >= minimumConfidence ? generated : null;
        var isDispatch = decision.DispatchProbability >= 0.5;
        return new AiDispatchEnrichment(isDispatch, Math.Max(decision.DispatchProbability, 1 - decision.DispatchProbability), text?.Title,
            Accept(decision.CallType, decision.CallTypeProbability), Accept(decision.Priority, decision.PriorityProbability),
            text?.Address, text?.ContactName, text?.ContactNumber, text?.IncidentNumber,
            Accept(decision.RelatedCall, decision.RelatedCallProbability), text?.Summary, generated?.RejectedCount ?? 0);
    }

    private static List<DecisionOption> Options(IEnumerable<DecisionOption> listed, string none) =>
        listed.Take(SystemOneDecisionClient.MaxOptions - 1).Prepend(new DecisionOption(None, none)).ToList();

    private static string Key(string prefix, int id) => prefix + id.ToString(CultureInfo.InvariantCulture);

    private static string Candidate(AiDispatchCandidateCall call) =>
        $"Call {call.Number}: {call.Name}{(string.IsNullOrWhiteSpace(call.Address) ? "" : ", " + call.Address)}, logged {call.MinutesAgo} minutes ago";

    // Descriptions are single-line and bounded; an empty name still needs a description the server accepts.
    private static string Label(string? value)
    {
        var line = string.Join(' ', (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length == 0) return "(unnamed)";
        return line.Length <= 200 ? line : line.Substring(0, 200);
    }
}
