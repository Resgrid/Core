namespace Resgrid.Llm;

/// <summary>Where the decision model runs. Both speak the SystemOne request and answer shape.</summary>
public static class DecisionProviders
{
    /// <summary>
    /// Cloudflare Workers AI REST (<c>/client/v4/accounts/{id}/ai/run/@cf/cloudflare/clef-flash</c>), directly or through an AI Gateway
    /// named by header; answers arrive in Cloudflare's <c>result</c> envelope. A third party: <see cref="DecisionDataClass.NonSensitive"/> and
    /// <see cref="DecisionDataClass.DepartmentConfiguration"/> requests only, never department content.
    /// </summary>
    public const string WorkersAi = "WorkersAi";
    /// <summary>The operator's own SGLang server at <c>/v1/systemone</c>; the only place department content may be scored.</summary>
    public const string SelfHosted = "SelfHosted";
}

/// <summary>Question kinds of a decision model (Cloudflare Clef / Clef-Flash on /v1/systemone): yes or no, one named option, or an ordered level.</summary>
public enum DecisionQuestionType { Noul, Choice, Score }

/// <summary>
/// One allowed answer. Choice ids are request-local keys listed in preference order (ties go to the earlier option). Score options
/// are levels lowest first and their ids must be "0".."n-1". A noul question takes no options, or exactly "true" and "false" to
/// describe them.
/// </summary>
public sealed record DecisionOption(string Id, string Description);

public sealed record DecisionQuestion(string Id, DecisionQuestionType Type, string Instructions, IReadOnlyList<DecisionOption>? Options = null);

/// <summary>What a decision request carries, which decides where it may go.</summary>
public enum DecisionDataClass
{
    /// <summary>
    /// Anything from a department: call messages, names, addresses, people, records. May contain PII or PHI however it is scrubbed,
    /// so it only ever goes to the operator's own (self-hosted) model. The default, so a new caller fails closed.
    /// </summary>
    DepartmentContent = 0,
    /// <summary>Built only from Resgrid's own reviewed text (catalog labels, fixed instructions); nothing a department or person wrote.</summary>
    NonSensitive = 1,
    /// <summary>
    /// Resgrid's own text plus names from department configuration lists that Advanced Data Protection does not protect (call
    /// types, priorities, unit types, roles, groups, status and staffing names — the registry is
    /// <c>Resgrid.Ai.AiNonPrivateFields</c>, kept outside the ADP catalog by test). Never a call, a person, a record, or any field in
    /// the ADP protected-field catalog: a call's own <c>Calls.Type</c> is catalogued, so it stays DepartmentContent.
    /// </summary>
    DepartmentConfiguration = 2
}

/// <summary>
/// One department's state (untrusted text) and the questions to score against it. The model generates nothing: every answer is a
/// probability over the options asked, so nothing outside them can come back. Third-party hosts (Workers AI) accept only
/// <see cref="DecisionDataClass.NonSensitive"/> and <see cref="DecisionDataClass.DepartmentConfiguration"/> requests.
/// </summary>
public sealed record DecisionRequest(string Model, string State, IReadOnlyList<DecisionQuestion> Questions, DecisionDataClass DataClass = DecisionDataClass.DepartmentContent);

/// <summary>Probabilities are keyed by option id ("true"/"false" for noul) and sum to one; <see cref="Choice"/> is the most probable option.</summary>
public sealed record DecisionAnswer(string Choice, double Confidence, IReadOnlyDictionary<string, double> Probabilities, double? Score = null)
{
    public double Probability(string optionId) => Probabilities.TryGetValue(optionId, out var value) ? value : 0;
}

/// <summary><see cref="InputTokens"/> is the server's count, or a conservative estimate when the server omits usage.</summary>
public sealed record DecisionResult(IReadOnlyDictionary<string, DecisionAnswer> Answers, int InputTokens);

public interface IDecisionClient
{
    Task<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken cancellationToken);
}
