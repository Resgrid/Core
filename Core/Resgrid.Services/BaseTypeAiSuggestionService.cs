using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Enhanced AI base-type suggestions (BaseTypeDecisions) for list entries the keyword suggester has no rule for. Anything a model
	/// produced is part of the Enhanced AI add-on, so the department must be entitled to <see cref="CapabilityFlag"/>, and the
	/// operator's decision model (AiConfig.Decision*) must resolve. Requests are DepartmentConfiguration only: built from list names
	/// read through AiNonPrivateFields, so Workers AI may score them; the transport refuses anything else. Not metered in the
	/// AiUsageLedger: that budget and its admission lock schedule the shared GPU, which Workers AI does not use, and a request costs
	/// a fraction of a cent. Answers are cached per name instead, so reloading a page does not ask again.
	/// </summary>
	public sealed class BaseTypeAiSuggestionService : IBaseTypeAiSuggestionService
	{
		/// <summary>The Enhanced AI capability that covers these suggestions: configuration help for administrators, like Admin Assist.</summary>
		public const string CapabilityFlag = FeatureFlagKeys.AiAdminAssist;
		/// <summary>A suggestion is shown only when the model's probability for it is at least this.</summary>
		public const double MinimumConfidence = 0.6;
		/// <summary>Distinct names asked about per call; a set or list with more unresolved entries is answered for the first ones.</summary>
		public const int MaxNames = 128;

		private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);
		private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
		private static readonly IReadOnlyDictionary<int, int> NoSuggestions = new Dictionary<int, int>();

		private readonly IEnhancedAiAccessService _enhancedAi;
		private readonly ICacheProvider _cache;
		private readonly Lazy<IDecisionClient> _decisions;

		public BaseTypeAiSuggestionService(IEnhancedAiAccessService enhancedAi, ICacheProvider cache, Lazy<IDecisionClient> decisions)
		{
			_enhancedAi = enhancedAi;
			_cache = cache;
			_decisions = decisions;
		}

		public Task<IReadOnlyDictionary<int, int>> SuggestCallTypeClassesAsync(int departmentId, IEnumerable<CallType> callTypes, CancellationToken cancellationToken) =>
			SuggestAsync(departmentId, BaseTypeList.CallType, (callTypes ?? Enumerable.Empty<CallType>())
				.Where(x => x != null && x.DepartmentId == departmentId && x.BaseType < 0 && BaseTypeSuggestions.SuggestCallTypeClass(x.Type) == null)
				.Select(x => (x.CallTypeId, (object)x)), cancellationToken);

		public Task<IReadOnlyDictionary<int, int>> SuggestUnitTypeClassesAsync(int departmentId, IEnumerable<UnitType> unitTypes, CancellationToken cancellationToken) =>
			SuggestAsync(departmentId, BaseTypeList.UnitType, (unitTypes ?? Enumerable.Empty<UnitType>())
				.Where(x => x != null && x.DepartmentId == departmentId && x.BaseType < 0 && BaseTypeSuggestions.SuggestUnitTypeClass(x.Type) == null)
				.Select(x => (x.UnitTypeId, (object)x)), cancellationToken);

		public Task<IReadOnlyDictionary<int, int>> SuggestCallPrioritiesAsync(int departmentId, IEnumerable<DepartmentCallPriority> priorities, CancellationToken cancellationToken) =>
			SuggestAsync(departmentId, BaseTypeList.CallPriority, (priorities ?? Enumerable.Empty<DepartmentCallPriority>())
				.Where(x => x != null && x.DepartmentId == departmentId && !x.IsDeleted && x.BaseType < 0 && BaseTypeSuggestions.SuggestPriority(x.Name) == null)
				.Select(x => (x.DepartmentCallPriorityId, (object)x)), cancellationToken);

		public Task<IReadOnlyDictionary<int, int>> SuggestStatusesAsync(int departmentId, CustomState state, CancellationToken cancellationToken)
		{
			var list = state == null ? null : BaseTypeDecisions.ListFor((CustomStateTypes)state.Type);
			if (list == null || state.DepartmentId != departmentId || state.IsDeleted)
				return Task.FromResult(NoSuggestions);

			var type = (CustomStateTypes)state.Type;
			return SuggestAsync(departmentId, list.Value, state.GetActiveDetails()
				.Where(x => x.BaseType < 0 && BaseTypeSuggestions.SuggestStatus(x.ButtonText, type) == null)
				.Select(x => (x.CustomStateDetailId, (object)x)), cancellationToken);
		}

		private async Task<IReadOnlyDictionary<int, int>> SuggestAsync(int departmentId, BaseTypeList list, IEnumerable<(int Id, object Entity)> pending, CancellationToken cancellationToken)
		{
			// Names are read only through the registry; an entry without one is never asked about.
			var entries = pending.Select(x => (x.Id, Label: BaseTypeDecisions.LabelOf(list, x.Entity)))
				.Where(x => x.Label != null)
				.Select(x => (x.Id, x.Label, Key: BaseTypeSuggestions.Normalize(x.Label)))
				.Where(x => x.Key.Length > 0)
				.ToList();
			if (departmentId <= 0 || entries.Count == 0)
				return NoSuggestions;

			var target = AiDecisionSettings.Resolve(AiConfig.DecisionProvider, AiConfig.DecisionEndpoint, AiConfig.AllowPrivateEndpoint, AiConfig.DecisionApiKey,
				AiConfig.DecisionModel, AiConfig.DecisionModelRevision, AiConfig.DecisionRuntimeDigest, AiConfig.DecisionGatewayId);
			if (target == null || !await _enhancedAi.CanUseAsync(departmentId, CapabilityFlag))
				return NoSuggestions;

			// One question per distinct name: the answer depends only on the name and the list, never on the department.
			var names = entries.GroupBy(x => x.Key, StringComparer.Ordinal).Take(MaxNames).Select(g => (g.Key, g.First().Label)).ToList();
			var answers = new Dictionary<string, BaseTypeAnswer>(StringComparer.Ordinal);
			var unanswered = new List<(string Key, string Label)>();
			foreach (var name in names)
			{
				var cached = Parse(list, await _cache.GetStringAsync(CacheKey(target, list, name.Key)));
				if (cached != null)
					answers[name.Key] = cached;
				else
					unanswered.Add(name);
			}

			if (unanswered.Count > 0)
				await AskAsync(target, list, unanswered, answers, cancellationToken);

			var suggestions = new Dictionary<int, int>();
			foreach (var entry in entries)
				if (answers.TryGetValue(entry.Key, out var answer) && answer.BaseType.HasValue && answer.Confidence >= MinimumConfidence)
					suggestions[entry.Id] = answer.BaseType.Value;
			return suggestions;
		}

		private async Task AskAsync(AiDecisionEndpoint target, BaseTypeList list, List<(string Key, string Label)> names, Dictionary<string, BaseTypeAnswer> answers, CancellationToken cancellationToken)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(Deadline);
			var keys = names.ToDictionary(x => x.Label, x => x.Key, StringComparer.Ordinal);
			foreach (var batch in BaseTypeDecisions.Batches(target.WireModel, names.Select(x => new BaseTypeQuestion(list, x.Label)).ToList()))
			{
				try
				{
					var result = await _decisions.Value.DecideAsync(BaseTypeDecisions.Request(target.WireModel, batch), deadline.Token);
					var read = BaseTypeDecisions.Read(result, batch);
					for (var i = 0; i < batch.Count; i++)
					{
						var key = keys[batch[i].Label];
						answers[key] = read[i];
						await _cache.SetStringAsync(CacheKey(target, list, key), Format(read[i]), CacheDuration);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex) when (ex is LlmUnavailableException || ex is ArgumentException || ex is OperationCanceledException)
				{
					// Unavailable, too slow or answered out of bounds: these names get no suggestion, and neither do the rest.
					return;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, "Enhanced AI base-type suggestions failed.");
					return;
				}
			}
		}

		// The model and question version are in the key, so a new model or wording asks again instead of reusing old answers.
		private static string CacheKey(AiDecisionEndpoint target, BaseTypeList list, string normalizedName)
		{
			var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedName))).Substring(0, 32);
			return $"AiBaseType_{BaseTypeDecisions.Version}_{target.AuditModel}_{list}_{hash}";
		}

		private static string Format(BaseTypeAnswer answer) =>
			(answer.BaseType ?? -1).ToString(CultureInfo.InvariantCulture) + ":" + answer.Confidence.ToString("R", CultureInfo.InvariantCulture);

		// Anything unreadable, or a meaning the list no longer offers, is asked again.
		private static BaseTypeAnswer Parse(BaseTypeList list, string value)
		{
			var parts = value?.Split(':');
			if (parts == null || parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var baseType) ||
				!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence) || confidence is < 0 or > 1 ||
				baseType >= 0 && !BaseTypeDecisions.Values(list).Contains(baseType))
				return null;
			return new BaseTypeAnswer(baseType < 0 ? null : baseType, confidence);
		}
	}
}
