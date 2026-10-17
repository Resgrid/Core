using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using Resgrid.Llm;
using Resgrid.Model;

namespace Resgrid.Ai;

/// <summary>The department list an entry belongs to: it decides the entry's allowed meanings and the registry field its name is read from.</summary>
public enum BaseTypeList { UnitStatus, PersonnelStatus, Staffing, CallType, UnitType, CallPriority }

/// <summary>One list entry's name, read through <see cref="BaseTypeDecisions.LabelOf"/>, to be given a meaning.</summary>
public sealed record BaseTypeQuestion(BaseTypeList List, string Label);

/// <summary>A meaning the model chose for one entry; <see cref="BaseType"/> is null when it chose "none".</summary>
public sealed record BaseTypeAnswer(int? BaseType, double Confidence);

/// <summary>
/// Enhanced AI base-type suggestions on a decision model (Cloudflare Clef / Clef-Flash): what a department's own status, staffing
/// level, call type, unit type or priority name means, for names the keyword suggester (BaseTypeSuggestions) has no rule for. One
/// choice question per entry over the list's base types plus "none"; the model can only score those options, so nothing outside
/// them can come back. Every request is <see cref="DecisionDataClass.DepartmentConfiguration"/>: the state and every option are
/// Resgrid's own text, and the only department text in it is each entry's name, read from exactly one
/// <see cref="AiNonPrivateFields.ConfigurationLabels"/> field. Never a call, a person or a record.
/// </summary>
public static class BaseTypeDecisions
{
    public const string Version = "base-type-suggest-v1";
    public const string None = "none";
    /// <summary>Names longer than this are cut; a list name is a short label and the question has a bounded size.</summary>
    public const int MaxLabelLength = 100;

    private const string State =
        "Resgrid is a dispatch and resource management system for fire, EMS, search and rescue, law enforcement and other emergency services. " +
        "Each department names its own list entries: unit and personnel statuses, staffing levels, call types, unit types and call priorities. " +
        "Each question gives the name of one entry and asks which standard Resgrid meaning it has. A name may be an abbreviation, local jargon or another language.";

    /// <summary>The one field each list's names are read from. A test keeps every entry in <see cref="AiNonPrivateFields.ConfigurationLabels"/>.</summary>
    public static IReadOnlyDictionary<BaseTypeList, AiNonPrivateField> Sources { get; } = new Dictionary<BaseTypeList, AiNonPrivateField>
    {
        [BaseTypeList.UnitStatus] = new(typeof(CustomStateDetail), nameof(CustomStateDetail.ButtonText)),
        [BaseTypeList.PersonnelStatus] = new(typeof(CustomStateDetail), nameof(CustomStateDetail.ButtonText)),
        [BaseTypeList.Staffing] = new(typeof(CustomStateDetail), nameof(CustomStateDetail.ButtonText)),
        [BaseTypeList.CallType] = new(typeof(CallType), nameof(CallType.Type)),
        [BaseTypeList.UnitType] = new(typeof(UnitType), nameof(UnitType.Type)),
        [BaseTypeList.CallPriority] = new(typeof(DepartmentCallPriority), nameof(DepartmentCallPriority.Name))
    };

    // "none" is listed first, so a tie goes to it and never invents a meaning.
    private static readonly IReadOnlyDictionary<BaseTypeList, IReadOnlyList<DecisionOption>> OptionsByList = new Dictionary<BaseTypeList, IReadOnlyList<DecisionOption>>
    {
        // A unit cannot be "not responding" (the keyword suggester maps it to Unavailable for units).
        [BaseTypeList.UnitStatus] = Options(Described<ActionBaseTypes>().Where(x => x.Value != (int)ActionBaseTypes.NotResponding)),
        [BaseTypeList.PersonnelStatus] = Options(Described<ActionBaseTypes>()),
        [BaseTypeList.Staffing] = Options(Described<StaffingBaseTypes>()),
        [BaseTypeList.CallType] = Options(Described<CallTypeClasses>()),
        [BaseTypeList.UnitType] = Options(Described<UnitTypeClasses>()),
        [BaseTypeList.CallPriority] = Options(new[]
        {
            ((int)CallPriority.Low, "Low: routine or non-urgent; no lights and siren."),
            ((int)CallPriority.Medium, "Medium: a prompt response is needed, but life is not threatened."),
            ((int)CallPriority.High, "High: urgent; a threat to life or property is possible."),
            ((int)CallPriority.Emergency, "Emergency: an immediate threat to life; the most urgent response.")
        })
    };

    public static BaseTypeList? ListFor(CustomStateTypes type) => type switch
    {
        CustomStateTypes.Unit => BaseTypeList.UnitStatus,
        CustomStateTypes.Personnel => BaseTypeList.PersonnelStatus,
        CustomStateTypes.Staffing => BaseTypeList.Staffing,
        _ => null
    };

    /// <summary>The base types a list's entries may be given, in option order (without "none").</summary>
    public static IReadOnlyList<int> Values(BaseTypeList list) =>
        OptionsByList[list].Where(o => o.Id != None).Select(o => int.Parse(o.Id.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture)).ToList();

    /// <summary>
    /// The entry's name, read only through the registry field for its list; null when the entity is not of that list's type, the
    /// field is no longer registered (ADP started protecting it) or the name is empty. Nothing else on the entity is ever read.
    /// </summary>
    public static string? LabelOf(BaseTypeList list, object? entity)
    {
        if (entity == null || !Sources.TryGetValue(list, out var source) || !source.Entity.IsInstanceOfType(entity) ||
            !AiNonPrivateFields.ConfigurationLabels.Contains(source))
            return null;
        var line = string.Join(' ', ((source.Entity.GetProperty(source.Property)?.GetValue(entity) as string) ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length == 0)
            return null;
        // The name sits inside the question's quotes; a quote of its own would end them.
        line = line.Replace('"', '\'');
        return line.Length <= MaxLabelLength ? line : line.Substring(0, MaxLabelLength);
    }

    public static DecisionRequest Request(string model, IReadOnlyList<BaseTypeQuestion> questions) =>
        new(model, State, questions.Select((q, i) => new DecisionQuestion(QuestionId(i), DecisionQuestionType.Choice, Instructions(q), OptionsByList[q.List])).ToList(),
            DecisionDataClass.DepartmentConfiguration);

    /// <summary>
    /// Splits the questions into requests the transport accepts: at most <see cref="SystemOneDecisionClient.MaxQuestions"/> each and
    /// within its byte bound (status questions carry about two dozen described options, so far fewer fit). A question that is out
    /// of bounds on its own is dropped.
    /// </summary>
    public static IEnumerable<IReadOnlyList<BaseTypeQuestion>> Batches(string model, IReadOnlyList<BaseTypeQuestion> questions)
    {
        var batch = new List<BaseTypeQuestion>();
        foreach (var question in questions)
        {
            batch.Add(question);
            if (batch.Count <= SystemOneDecisionClient.MaxQuestions && Fits(model, batch))
                continue;
            batch.RemoveAt(batch.Count - 1);
            if (batch.Count > 0)
                yield return batch;
            batch = Fits(model, new[] { question }) ? new List<BaseTypeQuestion> { question } : new List<BaseTypeQuestion>();
        }
        if (batch.Count > 0)
            yield return batch;
    }

    /// <summary>The answers in question order. Throws <see cref="ArgumentException"/> when one is missing or outside its options.</summary>
    public static IReadOnlyList<BaseTypeAnswer> Read(DecisionResult result, IReadOnlyList<BaseTypeQuestion> questions)
    {
        if (result?.Answers == null) throw new ArgumentException("No decision.");
        return questions.Select((q, i) =>
        {
            if (!result.Answers.TryGetValue(QuestionId(i), out var answer)) throw new ArgumentException("No decision.");
            if (answer.Choice == None) return new BaseTypeAnswer(null, answer.Confidence);
            var option = OptionsByList[q.List].FirstOrDefault(o => o.Id == answer.Choice) ?? throw new ArgumentException("Decision outside the list.");
            return new BaseTypeAnswer(int.Parse(option.Id.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture), answer.Confidence);
        }).ToList();
    }

    private static bool Fits(string model, IReadOnlyList<BaseTypeQuestion> questions)
    {
        try
        {
            SystemOneDecisionClient.Body(Request(model, questions));
            return true;
        }
        catch (LlmUnavailableException)
        {
            return false;
        }
    }

    private static string QuestionId(int index) => "q" + index.ToString(CultureInfo.InvariantCulture);

    private static string Instructions(BaseTypeQuestion question) => question.List switch
    {
        BaseTypeList.UnitStatus => $"A department named one of its unit statuses \"{question.Label}\". Which standard meaning does a unit in that status have? Choose none unless one clearly fits.",
        BaseTypeList.PersonnelStatus => $"A department named one of its personnel statuses (what a responder is doing) \"{question.Label}\". Which standard meaning does a responder in that status have? Choose none unless one clearly fits.",
        BaseTypeList.Staffing => $"A department named one of its staffing levels (whether a person can respond) \"{question.Label}\". Which standard meaning does that staffing level have? Choose none unless one clearly fits.",
        BaseTypeList.CallType => $"A department named one of its call types \"{question.Label}\". Which standard class of incident does that call type describe? Choose none unless one clearly fits.",
        BaseTypeList.UnitType => $"A department named one of its unit types \"{question.Label}\". Which standard class of apparatus or vehicle is that unit type? Choose none unless one clearly fits.",
        BaseTypeList.CallPriority => $"A department named one of its call priorities \"{question.Label}\". Which standard urgency does that priority mean? Choose none unless one clearly fits.",
        _ => throw new ArgumentOutOfRangeException(nameof(question))
    };

    private static IReadOnlyList<DecisionOption> Options(IEnumerable<(int Value, string Description)> values) =>
        values.Select(v => new DecisionOption("b" + v.Value.ToString(CultureInfo.InvariantCulture), v.Description))
            .Prepend(new DecisionOption(None, "None: no listed meaning clearly fits the name.")).ToList();

    // Resgrid's own reviewed text: each enum value's display name and the description shown on the edit screens.
    private static IEnumerable<(int Value, string Description)> Described<TEnum>() where TEnum : struct, Enum =>
        typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (Value: Convert.ToInt32(f.GetValue(null), CultureInfo.InvariantCulture), Field: f))
            .Where(x => x.Value >= 0)
            .OrderBy(x => x.Value)
            .Select(x => (x.Value, $"{x.Field.GetCustomAttribute<DisplayAttribute>()?.Name ?? x.Field.Name}: {x.Field.GetCustomAttribute<DescriptionAttribute>()?.Description ?? x.Field.Name}"));
}
