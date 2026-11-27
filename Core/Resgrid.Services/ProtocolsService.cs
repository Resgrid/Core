using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ProtocolsService : IProtocolsService
	{
		private static readonly string MatchingSetCacheKey = "ProtocolMatchingSet_{0}";
		private static readonly TimeSpan MatchingSetCacheLength = TimeSpan.FromDays(1);

		private readonly IDispatchProtocolRepository _dispatchProtocolRepository;
		private readonly IDispatchProtocolAttachmentRepository _dispatchProtocolAttachmentRepository;
		private readonly IDispatchProtocolQuestionsRepository _dispatchProtocolQuestionsRepository;
		private readonly IDispatchProtocolTriggersRepository _dispatchProtocolTriggersRepository;
		private readonly IDispatchProtocolQuestionAnswersRepository _dispatchProtocolQuestionAnswersRepository;
		private readonly Lazy<ISearchProjectionService> _searchProjections;
		private readonly ICacheProvider _cacheProvider;
		private readonly ICallTypesRepository _callTypesRepository;
		private readonly ICallProtocolsRepository _callProtocolsRepository;
		private readonly IDispatchProtocolSnapshotsRepository _snapshotsRepository;
		private readonly IProtocolVersionsRepository _versionsRepository;
		private readonly Lazy<IDispatchTriggerFactsService> _triggerFacts;
		private readonly Lazy<IProtocolAiSuggestionService> _aiSuggestions;

		private readonly Lazy<IProtectedWriteService> _protectedWrite;

		public ProtocolsService(IDispatchProtocolRepository dispatchProtocolRepository, IDispatchProtocolAttachmentRepository dispatchProtocolAttachmentRepository,
			IDispatchProtocolQuestionsRepository dispatchProtocolQuestionsRepository, IDispatchProtocolTriggersRepository dispatchProtocolTriggersRepository,
			IDispatchProtocolQuestionAnswersRepository dispatchProtocolQuestionAnswersRepository, Lazy<IDispatchTriggerFactsService> triggerFacts,
			Lazy<IProtocolAiSuggestionService> aiSuggestions, Lazy<IProtectedWriteService> protectedWrite, Lazy<ISearchProjectionService> searchProjections = null,
			ICacheProvider cacheProvider = null, ICallTypesRepository callTypesRepository = null, ICallProtocolsRepository callProtocolsRepository = null,
			IDispatchProtocolSnapshotsRepository snapshotsRepository = null, IProtocolVersionsRepository versionsRepository = null)
		{
			_protectedWrite = protectedWrite;
			_versionsRepository = versionsRepository;
			_triggerFacts = triggerFacts;
			_aiSuggestions = aiSuggestions;
			_searchProjections = searchProjections;
			_dispatchProtocolRepository = dispatchProtocolRepository;
			_dispatchProtocolAttachmentRepository = dispatchProtocolAttachmentRepository;
			_dispatchProtocolQuestionsRepository = dispatchProtocolQuestionsRepository;
			_dispatchProtocolTriggersRepository = dispatchProtocolTriggersRepository;
			_dispatchProtocolQuestionAnswersRepository = dispatchProtocolQuestionAnswersRepository;
			_cacheProvider = cacheProvider;
			_callTypesRepository = callTypesRepository;
			_callProtocolsRepository = callProtocolsRepository;
			_snapshotsRepository = snapshotsRepository;
		}

		#region Reading

		public Task<List<DispatchProtocol>> GetAllProtocolsForDepartmentAsync(int departmentId)
		{
			return GetProtocolSummariesForDepartmentAsync(departmentId);
		}

		public async Task<List<DispatchProtocol>> GetProtocolSummariesForDepartmentAsync(int departmentId, bool enabledOnly = false)
		{
			var protocols = (await LoadDepartmentProtocolsAsync(departmentId)).Where(x => !x.IsDeleted);

			// "In force" (P1): enabled and with a published version. Drafts never published and retired protocols are the
			// library's alone; the apps, the offline reference data and every attach path see only what is published.
			if (enabledOnly)
				protocols = protocols.Where(x => !x.IsDisabled && x.IsPublished);

			return protocols.ToList();
		}

		/// <summary>
		/// The department's protocol rows (deleted ones too) with their triggers, questions, answers and attachment info, in four
		/// queries for the whole department. Attachment bytes are never read here.
		/// </summary>
		private async Task<List<DispatchProtocol>> LoadDepartmentProtocolsAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<DispatchProtocol>();

			// The generic department query has returned other departments' rows before; ownership never rests on it alone.
			var protocols = (await _dispatchProtocolRepository.GetAllByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatchProtocol>())
				.Where(x => x != null && x.DepartmentId == departmentId)
				.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();

			if (protocols.Count == 0)
				return protocols;

			var triggers = (await _dispatchProtocolTriggersRepository.GetTriggersByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatchProtocolTrigger>())
				.ToLookup(x => x.DispatchProtocolId);
			var questions = (await _dispatchProtocolQuestionsRepository.GetQuestionsWithAnswersByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatchProtocolQuestion>())
				.ToLookup(x => x.DispatchProtocolId);
			var attachments = (await _dispatchProtocolAttachmentRepository.GetAttachmentInfoByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatchProtocolAttachment>())
				.ToLookup(x => x.DispatchProtocolId);

			foreach (var protocol in protocols)
			{
				protocol.Triggers = triggers[protocol.DispatchProtocolId].ToList();
				protocol.Questions = questions[protocol.DispatchProtocolId].Select(WithAnswers).ToList();
				protocol.Attachments = attachments[protocol.DispatchProtocolId].ToList();
			}

			return protocols;
		}

		private static DispatchProtocolQuestion WithAnswers(DispatchProtocolQuestion question)
		{
			if (question.Answers == null)
				question.Answers = new List<DispatchProtocolQuestionAnswer>();

			return question;
		}

		public async Task<List<DispatchProtocol>> GetMatchingSetAsync(int departmentId, bool bypassCache = false)
		{
			async Task<List<DispatchProtocol>> build()
			{
				// What the matcher and the question flow need, and nothing else: no text, no description, no attachments. Only the
				// kinds whose triggers mean something (Dispatch, Procedure) are matched to calls.
				return (await GetProtocolSummariesForDepartmentAsync(departmentId, true)).Where(x => ProtocolKindRules.TriggersApply(x.Kind)).Select(x => new DispatchProtocol
				{
					DispatchProtocolId = x.DispatchProtocolId,
					DepartmentId = x.DepartmentId,
					Name = x.Name,
					Code = x.Code,
					MinimumWeight = x.MinimumWeight,
					IsDisabled = x.IsDisabled,
					Kind = x.Kind,
					Status = x.Status,
					CurrentVersionId = x.CurrentVersionId,
					CreatedOn = x.CreatedOn,
					UpdatedOn = x.UpdatedOn,
					Triggers = x.Triggers,
					Questions = x.Questions,
					Attachments = new List<DispatchProtocolAttachment>()
				}).ToList();
			}

			if (bypassCache || _cacheProvider == null || !Config.SystemBehaviorConfig.CacheEnabled)
				return await build();

			// The cache provider serializes with protobuf-net and the protocol entities are not contracts, so the set is cached as
			// JSON inside the same envelope the offline reference data uses.
			var envelope = await _cacheProvider.RetrieveAsync<ReferenceCacheEnvelope>(string.Format(MatchingSetCacheKey, departmentId),
				async () => new ReferenceCacheEnvelope { Json = JsonConvert.SerializeObject(await build()) }, MatchingSetCacheLength);

			if (!string.IsNullOrEmpty(envelope?.Json))
			{
				var cached = JsonConvert.DeserializeObject<List<DispatchProtocol>>(envelope.Json);
				if (cached != null)
					return cached;
			}

			return await build();
		}

		public async Task<List<int>> GetProtocolIdsOwnedByDepartmentAsync(int departmentId, IEnumerable<int> protocolIds)
		{
			var requested = protocolIds?.Distinct().ToList() ?? new List<int>();

			if (departmentId <= 0 || requested.Count == 0)
				return new List<int>();

			// The department's own protocol rows only: no attachments, questions or triggers are needed to check ownership.
			// A deleted or disabled protocol is not attached to a call.
			var attachable = (await _dispatchProtocolRepository.GetAllByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatchProtocol>())
				.Where(x => x.DepartmentId == departmentId && !x.IsDeleted && !x.IsDisabled && x.IsPublished)
				.Select(x => x.DispatchProtocolId)
				.ToHashSet();

			return requested.Where(attachable.Contains).ToList();
		}

		public async Task<DispatchProtocol> GetProtocolByIdAsync(int id)
		{
			var protocol = await _dispatchProtocolRepository.GetDispatchProtocolByIdAsync(id);

			if (protocol != null)
			{
				var attachments = await _dispatchProtocolAttachmentRepository.GetAttachmentInfoByProtocolIdAsync(id);
				protocol.Attachments = attachments?.ToList() ?? new List<DispatchProtocolAttachment>();

				var questions = await _dispatchProtocolQuestionsRepository.GetDispatchProtocolQuestionsByProtocolIdAsync(id);
				protocol.Questions = questions?.Select(WithAnswers).ToList() ?? new List<DispatchProtocolQuestion>();

				var triggers = await _dispatchProtocolTriggersRepository.GetDispatchProtocolTriggersByProtocolIdAsync(id);
				protocol.Triggers = triggers?.ToList() ?? new List<DispatchProtocolTrigger>();
			}

			return protocol;
		}

		public async Task<DispatchProtocol> GetProtocolHeaderByIdAsync(int id)
		{
			if (id <= 0)
				return null;

			return await _dispatchProtocolRepository.GetByIdAsync(id);
		}

		public async Task<DispatchProtocolAttachment> GetAttachmentByIdAsync(int protocolAttachmentId)
		{
			return await _dispatchProtocolAttachmentRepository.GetByIdAsync(protocolAttachmentId);
		}

		public async Task<DispatchProtocolAttachment> GetAttachmentForDepartmentAsync(int departmentId, int protocolAttachmentId)
		{
			if (departmentId <= 0 || protocolAttachmentId <= 0)
				return null;

			var attachment = await _dispatchProtocolAttachmentRepository.GetByIdAsync(protocolAttachmentId);
			if (attachment == null)
				return null;

			var protocol = await GetProtocolHeaderByIdAsync(attachment.DispatchProtocolId);
			if (protocol == null || protocol.DepartmentId != departmentId)
				return null;

			return attachment;
		}

		public async Task<List<ProtocolVersion>> GetCitedVersionsAsync(int departmentId, IEnumerable<int> protocolVersionIds)
		{
			var ids = (protocolVersionIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct().ToList();
			if (_versionsRepository == null || departmentId <= 0 || ids.Count == 0)
				return new List<ProtocolVersion>();

			return (await _versionsRepository.GetByIdsAsync(departmentId, ids, true) ?? new List<ProtocolVersion>())
				.Where(x => x.DepartmentId == departmentId)
				.ToList();
		}

		public async Task<List<DispatchProtocolSnapshot>> GetSnapshotsAsync(int departmentId, IEnumerable<string> contentHashes)
		{
			if (_snapshotsRepository == null || departmentId <= 0)
				return new List<DispatchProtocolSnapshot>();

			var hashes = (contentHashes ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
			if (hashes.Count == 0)
				return new List<DispatchProtocolSnapshot>();

			return (await _snapshotsRepository.GetByHashesAsync(departmentId, hashes) ?? Enumerable.Empty<DispatchProtocolSnapshot>())
				.Where(x => x.DepartmentId == departmentId)
				.ToList();
		}

		public async Task<Dictionary<int, int>> GetUsageCountsAsync(int departmentId, DateTime sinceUtc)
		{
			if (_callProtocolsRepository == null || departmentId <= 0)
				return new Dictionary<int, int>();

			return (await _callProtocolsRepository.GetUsageCountsAsync(departmentId, sinceUtc) ?? Enumerable.Empty<ProtocolUsageCount>())
				.GroupBy(x => x.DispatchProtocolId)
				.ToDictionary(x => x.Key, x => x.Sum(y => y.Calls));
		}

		#endregion Reading

		#region Matching

		public async Task<ProtocolCallContext> BuildCallContextAsync(int departmentId, int priority, int? callTypeId, string callTypeName, DateTime? loggedOnUtc)
		{
			var context = new ProtocolCallContext
			{
				Priority = priority,
				CallTypeName = callTypeName,
				AtUtc = loggedOnUtc.HasValue && loggedOnUtc.Value > DateTime.MinValue.AddDays(1) ? DateTime.SpecifyKind(loggedOnUtc.Value, DateTimeKind.Utc) : DateTime.UtcNow
			};

			if (_callTypesRepository == null || departmentId <= 0)
			{
				context.CallTypeId = callTypeId;
				return context;
			}

			var types = (await _callTypesRepository.GetAllByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<CallType>())
				.Where(x => x != null && x.DepartmentId == departmentId)
				.ToList();

			// An id the department has wins; otherwise the name. No match (a blank name, the translated "No Type" label, a type
			// since deleted) is "no type".
			if (callTypeId.HasValue && types.Any(x => x.CallTypeId == callTypeId.Value))
				context.CallTypeId = callTypeId.Value;
			else
				context.CallTypeId = CallTypeLink.Resolve(callTypeName, null, types);

			return context;
		}

		public async Task<ProtocolCallContext> BuildCallContextWithFactsAsync(int departmentId, int priority, int? callTypeId, string callTypeName, DispatchTriggerCallInput details)
		{
			var context = await BuildCallContextAsync(departmentId, priority, callTypeId, callTypeName, details?.LoggedOnUtc);

			// P4: the facts the shared trigger engine reads besides type, priority and time (zone, local schedule, keywords, POI type,
			// intake source). Best effort: a fact that cannot be resolved only stops the criteria that need it from matching.
			if (_triggerFacts != null && details != null)
				await _triggerFacts.Value.FillAsync(departmentId, context, details);

			return context;
		}

		public async Task<List<ProtocolMatch>> GetProtocolsForCallAsync(int departmentId, ProtocolCallContext call)
		{
			var matches = new List<ProtocolMatch>();
			if (call == null)
				return matches;

			foreach (var protocol in await GetMatchingSetAsync(departmentId))
			{
				var triggers = ProtocolTriggerMatcher.Match(protocol, call);
				var match = new ProtocolMatch { Protocol = protocol, MatchedTrigger = ProtocolTriggerMatcher.MostSpecific(triggers) };

				if (match.MatchedTrigger == null)
					match.State = ProtocolStates.Inactive;
				else if (protocol.Questions != null && protocol.Questions.Any())
					match.State = ProtocolStates.Pending;
				else
					match.State = ProtocolStates.Active;

				matches.Add(match);
			}

			return matches;
		}

		public List<DispatchProtocol> ProcessTriggers(List<DispatchProtocol> protocols, Call call)
		{
			if (protocols == null || call == null)
				return null;

			foreach (var protocol in protocols)
			{
				var activeTriggers = DetermineActiveTriggers(protocol, call);

				if (activeTriggers != null && activeTriggers.Any())
				{
					if (protocol.Questions != null && protocol.Questions.Any())
						protocol.State = ProtocolStates.Pending;
					else
						protocol.State = ProtocolStates.Active;
				}
				else
				{
					protocol.State = ProtocolStates.Inactive;
				}
			}

			return protocols;
		}

		public List<DispatchProtocolTrigger> DetermineActiveTriggers(DispatchProtocol protocol, Call call)
		{
			if (protocol == null || protocol.Triggers == null || !protocol.Triggers.Any())
				return null;

			if (call == null)
				return null;

			if (protocol.IsDisabled || protocol.IsDeleted)
				return null;

			var context = new ProtocolCallContext
			{
				Priority = call.Priority,
				CallTypeId = call.CallTypeId,
				CallTypeName = call.Type,
				AtUtc = call.LoggedOn > DateTime.MinValue.AddDays(1) ? call.LoggedOn : DateTime.UtcNow
			};

			// No trigger inside its window at the call's time: nothing could apply.
			if (!protocol.Triggers.Any(x => ProtocolTriggerMatcher.InWindow(x, context.AtUtc)))
				return null;

			return ProtocolTriggerMatcher.Match(protocol, context);
		}

		#endregion Matching

		#region The call record

		public async Task<ProtocolAttachResult> PrepareCallProtocolsAsync(int departmentId, Call call, IEnumerable<ProtocolAttachRequest> requests, string userId,
			IEnumerable<CallProtocol> alreadyOnCall = null, bool storeSnapshots = true, CancellationToken cancellationToken = default(CancellationToken))
		{
			var result = new ProtocolAttachResult();
			var asked = (requests ?? Enumerable.Empty<ProtocolAttachRequest>()).Where(x => x != null && x.ProtocolId > 0).ToList();

			if (departmentId <= 0 || call == null || asked.Count == 0)
				return result;

			var onCall = new HashSet<int>((alreadyOnCall ?? call.Protocols ?? Enumerable.Empty<CallProtocol>()).Select(x => x.DispatchProtocolId));
			var protocols = (await LoadDepartmentProtocolsAsync(departmentId)).ToDictionary(x => x.DispatchProtocolId);
			var context = await BuildCallContextWithFactsAsync(departmentId, call.Priority, call.CallTypeId, call.Type, DispatchTriggerCallInput.FromCall(call));
			var now = DateTime.UtcNow;
			// P4: the decision graphs of the versions in force of the protocols asked for (one query; only valid graphs count).
			var graphs = await LoadGraphsAsync(departmentId, asked.Select(x => protocols.TryGetValue(x.ProtocolId, out var p) ? p : null));

			foreach (var request in asked)
			{
				ProtocolAttachRefusals? refusal = null;

				if (!protocols.TryGetValue(request.ProtocolId, out var protocol) || protocol.DepartmentId != departmentId)
					refusal = ProtocolAttachRefusals.NotFound;
				else if (protocol.IsDeleted)
					refusal = ProtocolAttachRefusals.Deleted;
				else if (protocol.IsDisabled)
					refusal = ProtocolAttachRefusals.Disabled;
				else if (!protocol.IsPublished)
					refusal = ProtocolAttachRefusals.NotPublished;
				else if (!onCall.Add(protocol.DispatchProtocolId))
					refusal = ProtocolAttachRefusals.Duplicate;

				if (refusal.HasValue)
				{
					result.Refused.Add(new ProtocolAttachRefusal { ProtocolId = request.ProtocolId, Reason = refusal.Value });
					continue;
				}

				var matched = ProtocolTriggerMatcher.MostSpecific(ProtocolTriggerMatcher.Match(protocol, context));
				var source = matched != null ? CallProtocolSources.Trigger : CallProtocolSources.Manual;
				var answers = ScoreAnswers(protocol, request.AnswerIds, matched);
				var hasQuestions = protocol.Questions != null && protocol.Questions.Any();

				// A suggested protocol with questions applies only once its questions say so; a protocol picked by hand is the
				// dispatcher's call, and its answers (if any) are recorded as given.
				if (hasQuestions && request.AnswersRequired && source == CallProtocolSources.Trigger)
				{
					if (answers == null || !answers.Complete)
						refusal = ProtocolAttachRefusals.QuestionsUnanswered;
					else if (!answers.Passed)
						refusal = ProtocolAttachRefusals.BelowMinimumWeight;
				}

				if (refusal.HasValue)
				{
					onCall.Remove(protocol.DispatchProtocolId);
					result.Refused.Add(new ProtocolAttachRefusal { ProtocolId = request.ProtocolId, Reason = refusal.Value });
					continue;
				}

				// P4: the case flow. The server walks the cited version's graph with the posted answers; the client decides nothing but
				// the answers and an override, which needs a reason. A confirmed Enhanced AI suggestion is recorded as such, once.
				CallProtocolCase caseRecord = null;
				var callSource = source;
				if (request.Case != null && protocol.CurrentVersionId.HasValue && graphs.TryGetValue(protocol.CurrentVersionId.Value, out var graph))
				{
					ProtocolAiSuggestion suggestion = null;
					if (request.Case.AiSuggestionId.HasValue && call.CallId > 0 && _aiSuggestions != null)
						suggestion = await _aiSuggestions.Value.GetConfirmableAsync(departmentId, call.CallId, protocol.DispatchProtocolId, request.Case.AiSuggestionId.Value);

					var walked = ProtocolCaseFlow.Run(graph, request.Case.Answers);
					var (record, caseError) = ProtocolCaseFlow.Record(walked, request.Case, protocol.CurrentVersionId,
						suggestion != null ? CallProtocolSources.Ai : CallProtocolSources.Manual, userId, now);

					if (caseError == ProtocolCaseErrors.OverrideReasonRequired)
						refusal = ProtocolAttachRefusals.OverrideReasonRequired;
					else if (caseError != ProtocolCaseErrors.None)
						refusal = ProtocolAttachRefusals.CaseInvalid;

					if (refusal.HasValue)
					{
						onCall.Remove(protocol.DispatchProtocolId);
						result.Refused.Add(new ProtocolAttachRefusal { ProtocolId = request.ProtocolId, Reason = refusal.Value });
						continue;
					}

					record.ProtocolCode = protocol.Code;
					record.ProtocolName = protocol.Name;
					caseRecord = record;

					if (suggestion != null)
					{
						callSource = CallProtocolSources.Ai;
						if (storeSnapshots)
							await _aiSuggestions.Value.ConfirmAsync(departmentId, suggestion.ProtocolAiSuggestionId, userId, cancellationToken);
					}
				}

				var hash = ProtocolContentHash.Compute(protocol.ProtocolText);

				result.Attached.Add(new CallProtocol
				{
					CallId = call.CallId,
					DispatchProtocolId = protocol.DispatchProtocolId,
					// The code responders see in the dispatch e-mail and text is the protocol's own, never a posted value.
					Data = protocol.Code,
					Score = answers?.Score ?? 0,
					Trigger = matched?.Type ?? 0,
					// A trigger match stays Trigger; a protocol picked by hand that a dispatcher confirmed from an AI suggestion is Ai.
					Source = (int)(source == CallProtocolSources.Trigger ? source : callSource),
					Determinant = caseRecord?.Determinant,
					OverrideReason = caseRecord != null && caseRecord.Overridden ? caseRecord.OverrideReason : null,
					CaseJson = caseRecord?.ToJson(),
					AnswersJson = answers == null ? null : JsonConvert.SerializeObject(answers),
					ContentHash = hash,
					AttachedOn = now,
					AttachedByUserId = userId,
					// Calls cite versions, never live rows (P1 §4): the version in force when it was attached.
					ProtocolVersionId = protocol.CurrentVersionId
				});

				if (storeSnapshots && _snapshotsRepository != null)
				{
					await _snapshotsRepository.InsertIfMissingAsync(new DispatchProtocolSnapshot
					{
						DepartmentId = departmentId,
						DispatchProtocolId = protocol.DispatchProtocolId,
						ContentHash = hash,
						ProtocolText = protocol.ProtocolText,
						FirstUsedOn = now
					}, cancellationToken);
				}
			}

			return result;
		}

		/// <summary>
		/// The valid decision graphs (P4) of the versions in force of the given protocols, by version id. A protocol that is not Dispatch kind,
		/// has no version in force, or whose version has no graph or an invalid one, has none.
		/// </summary>
		private async Task<Dictionary<int, ProtocolDecisionGraph>> LoadGraphsAsync(int departmentId, IEnumerable<DispatchProtocol> protocols)
		{
			var graphs = new Dictionary<int, ProtocolDecisionGraph>();
			var versionIds = (protocols ?? Enumerable.Empty<DispatchProtocol>())
				.Where(p => p != null && p.Kind == (int)ProtocolKinds.Dispatch && p.CurrentVersionId.HasValue)
				.Select(p => p.CurrentVersionId.Value)
				.Distinct()
				.ToList();

			if (_versionsRepository == null || versionIds.Count == 0)
				return graphs;

			foreach (var version in await _versionsRepository.GetByIdsAsync(departmentId, versionIds, true) ?? new List<ProtocolVersion>())
			{
				if (version == null || version.DepartmentId != departmentId || version.IsDocument)
					continue;

				var graph = ProtocolContentDocument.Parse(version.ContentJson, null).Graph;
				if (graph != null && !graph.IsEmpty && ProtocolDecisionGraphs.Validate(graph).Count == 0)
					graphs[version.ProtocolVersionId] = graph;
			}

			return graphs;
		}

		/// <summary>
		/// The questionnaire in words from the chosen answer ids. Each question takes the first chosen id that is one of its own
		/// answers; ids from other questions or protocols are ignored. Null when the protocol has no questions or nothing was sent.
		/// </summary>
		public static CallProtocolAnswers ScoreAnswers(DispatchProtocol protocol, IEnumerable<int> answerIds, DispatchProtocolTrigger matched)
		{
			var questions = protocol?.Questions?.Where(x => x != null).ToList() ?? new List<DispatchProtocolQuestion>();
			if (questions.Count == 0 || answerIds == null)
				return null;

			var chosen = new HashSet<int>(answerIds);
			var record = new CallProtocolAnswers
			{
				ProtocolCode = protocol.Code,
				ProtocolName = protocol.Name,
				MinimumWeight = protocol.MinimumWeight,
				MatchedTriggerId = matched?.DispatchProtocolTriggerId,
				MatchedTriggerType = matched?.Type
			};

			foreach (var question in questions.OrderBy(x => x.DispatchProtocolQuestionId))
			{
				var answer = (question.Answers ?? Enumerable.Empty<DispatchProtocolQuestionAnswer>())
					.OrderBy(x => x.DispatchProtocolQuestionAnswerId)
					.FirstOrDefault(x => chosen.Contains(x.DispatchProtocolQuestionAnswerId));

				record.Answers.Add(new CallProtocolAnswer
				{
					QuestionId = question.DispatchProtocolQuestionId,
					Question = question.Question,
					AnswerId = answer?.DispatchProtocolQuestionAnswerId,
					Answer = answer?.Answer,
					Weight = answer?.Weight ?? 0
				});
			}

			record.Score = record.Answers.Sum(x => x.Weight);
			record.Complete = record.Answers.All(x => x.AnswerId.HasValue);
			record.Passed = record.Complete && record.Score >= protocol.MinimumWeight;

			return record;
		}

		public async Task<CallProtocol> RemoveProtocolFromCallAsync(int departmentId, int callId, int callProtocolId, string reason, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (_callProtocolsRepository == null || departmentId <= 0 || callId <= 0 || callProtocolId <= 0 || string.IsNullOrWhiteSpace(reason))
				return null;

			var row = await _callProtocolsRepository.GetByIdAsync(callProtocolId);
			if (row == null || row.CallId != callId)
				return null;

			var protocol = await GetProtocolHeaderByIdAsync(row.DispatchProtocolId);
			if (protocol == null || protocol.DepartmentId != departmentId)
				return null;

			if (row.RemovedOn.HasValue)
				return row;

			var text = reason.Trim();
			row.RemovedOn = DateTime.UtcNow;
			row.RemovedByUserId = userId;
			row.RemovedReason = text.Length > ProtocolRules.RemovalReasonMaxLength ? text.Substring(0, ProtocolRules.RemovalReasonMaxLength) : text;

			// ADP catalog 39: the removal reason is free text about the call. It is enveloped before the row is written (the other protected
			// columns are written back as they were read), and the caller gets the row with the reason it typed.
			var typed = row.RemovedReason;
			row.RemovedReason = await ProtectReasonAsync(departmentId, row, cancellationToken);
			var stored = await _callProtocolsRepository.SaveOrUpdateAsync(row, cancellationToken, true);
			row.RemovedReason = typed;
			if (stored != null)
				stored.RemovedReason = typed;
			return stored;
		}

		/// <summary>The removal reason as it may be stored: enveloped for a department that encrypts new writes, else as typed.</summary>
		private async Task<string> ProtectReasonAsync(int departmentId, CallProtocol row, CancellationToken cancellationToken)
		{
			if (_protectedWrite == null || string.IsNullOrEmpty(row.RemovedReason))
				return row.RemovedReason;

			var probe = new CallProtocol { CallProtocolId = row.CallProtocolId, RemovedReason = row.RemovedReason };
			var write = await _protectedWrite.Value.PrepareRecordsEntityWriteAsync(departmentId, probe, null, row.CallProtocolId.ToString(System.Globalization.CultureInfo.InvariantCulture),
				ContentProtectedFields.CallProtocols, null, null, null, workloadCaller: true, cancellationToken);
			if (write != null && !write.Success)
				throw new InvalidOperationException($"Protected write blocked ({write.Reason}); call protocol {row.CallProtocolId} was not removed.");
			return probe.RemovedReason;
		}

		#endregion The call record

		#region Call types and priorities

		public async Task<int> RenameCallTypeInTriggersAsync(int departmentId, string oldName, string newName, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) || CallTypeLink.SameName(oldName, newName))
				return 0;

			var renamed = 0;
			foreach (var trigger in await DepartmentTriggersAsync(departmentId))
			{
				if (!trigger.CallTypeId.HasValue && CallTypeLink.SameName(trigger.CallType, oldName))
				{
					trigger.CallType = newName;
					await _dispatchProtocolTriggersRepository.SaveOrUpdateAsync(trigger, cancellationToken, true);
					renamed++;
				}
			}

			if (renamed > 0)
				await InvalidateProtocolCachesAsync(departmentId);

			return renamed;
		}

		public async Task<int> RenameCallTypeInTriggersAsync(int departmentId, int callTypeId, string oldName, string newName, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (string.IsNullOrWhiteSpace(newName))
				return 0;

			var renamed = 0;
			foreach (var trigger in await DepartmentTriggersAsync(departmentId))
			{
				var byId = trigger.CallTypeId.HasValue && trigger.CallTypeId.Value == callTypeId;
				var byOldName = !trigger.CallTypeId.HasValue && !string.IsNullOrWhiteSpace(oldName) && CallTypeLink.SameName(trigger.CallType, oldName);

				if (!byId && !byOldName)
					continue;

				if (byId && string.Equals(trigger.CallType, newName, StringComparison.Ordinal))
					continue;

				trigger.CallType = newName;
				trigger.CallTypeId = callTypeId;
				await _dispatchProtocolTriggersRepository.SaveOrUpdateAsync(trigger, cancellationToken, true);
				renamed++;
			}

			if (renamed > 0)
				await InvalidateProtocolCachesAsync(departmentId);

			return renamed;
		}

		public async Task<List<ProtocolTriggerImpact>> GetTriggersForCallTypeAsync(int departmentId, int callTypeId, string callTypeName)
		{
			return (await LoadDepartmentProtocolsAsync(departmentId))
				.Where(x => !x.IsDeleted)
				.SelectMany(p => (p.Triggers ?? Enumerable.Empty<DispatchProtocolTrigger>())
					.Where(t => t.Type == (int)ProtocolTriggerTypes.CallType || t.Type == (int)ProtocolTriggerTypes.CallPriortyAndType)
					.Where(t => t.CallTypeId.HasValue
						? t.CallTypeId.Value == callTypeId
						: !ProtocolTriggerMatcher.IsNoTypeTrigger(t) && CallTypeLink.SameName(t.CallType, callTypeName))
					.Select(t => Impact(p, t)))
				.ToList();
		}

		public async Task<List<ProtocolTriggerImpact>> GetTriggersForPriorityAsync(int departmentId, int priorityId)
		{
			return (await LoadDepartmentProtocolsAsync(departmentId))
				.Where(x => !x.IsDeleted)
				.SelectMany(p => (p.Triggers ?? Enumerable.Empty<DispatchProtocolTrigger>())
					.Where(t => (t.Type == (int)ProtocolTriggerTypes.CallPriorty || t.Type == (int)ProtocolTriggerTypes.CallPriortyAndType) && t.Priority == priorityId)
					.Select(t => Impact(p, t)))
				.ToList();
		}

		private static ProtocolTriggerImpact Impact(DispatchProtocol protocol, DispatchProtocolTrigger trigger) => new ProtocolTriggerImpact
		{
			ProtocolId = protocol.DispatchProtocolId,
			ProtocolCode = protocol.Code,
			ProtocolName = protocol.Name,
			TriggerId = trigger.DispatchProtocolTriggerId,
			TriggerType = trigger.Type
		};

		private async Task<List<DispatchProtocolTrigger>> DepartmentTriggersAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<DispatchProtocolTrigger>();

			var protocolIds = (await _dispatchProtocolRepository.GetAllByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatchProtocol>())
				.Where(x => x.DepartmentId == departmentId)
				.Select(x => x.DispatchProtocolId)
				.ToHashSet();

			return (await _dispatchProtocolTriggersRepository.GetTriggersByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatchProtocolTrigger>())
				.Where(x => protocolIds.Contains(x.DispatchProtocolId))
				.ToList();
		}

		#endregion Call types and priorities

		#region Authoring

		public async Task<DispatchProtocol> SaveProtocolAsync(DispatchProtocol protocol, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (protocol.DispatchProtocolId > 0)
				return await UpdateProtocolAsync(protocol, null, protocol.UpdatedByUserId, cancellationToken);

			// Rich text the call views render as HTML: scripts, handlers and frames are never stored.
			protocol.Description = StringHelpers.SanitizeHtmlForStorage(protocol.Description);
			protocol.ProtocolText = StringHelpers.SanitizeHtmlForStorage(protocol.ProtocolText);
			protocol.Code = ProtocolRules.NormalizeCode(protocol.Code);
			protocol.Name = protocol.Name?.Trim();

			var triggers = protocol.Triggers?.ToList() ?? new List<DispatchProtocolTrigger>();
			var questions = protocol.Questions?.ToList() ?? new List<DispatchProtocolQuestion>();
			var attachments = protocol.Attachments?.ToList() ?? new List<DispatchProtocolAttachment>();

			// The row first, alone: the children are written explicitly below, never by the repository's cascade.
			var saved = await _dispatchProtocolRepository.SaveOrUpdateAsync(protocol, cancellationToken, true);

			foreach (var trigger in triggers)
			{
				trigger.DispatchProtocolTriggerId = 0;
				trigger.DispatchProtocolId = saved.DispatchProtocolId;
				await _dispatchProtocolTriggersRepository.SaveOrUpdateAsync(trigger, cancellationToken, true);
			}

			foreach (var question in questions)
				await InsertQuestionAsync(saved.DispatchProtocolId, question, cancellationToken);

			foreach (var attachment in attachments.Where(x => x.Data != null))
			{
				attachment.DispatchProtocolAttachmentId = 0;
				attachment.DispatchProtocolId = saved.DispatchProtocolId;
				await _dispatchProtocolAttachmentRepository.SaveOrUpdateAsync(attachment, cancellationToken, true);
			}

			saved.Triggers = triggers;
			saved.Questions = questions;
			saved.Attachments = attachments;

			await AfterChangeAsync(saved, cancellationToken);
			return saved;
		}

		private async Task InsertQuestionAsync(int protocolId, DispatchProtocolQuestion question, CancellationToken cancellationToken)
		{
			var answers = question.Answers?.ToList() ?? new List<DispatchProtocolQuestionAnswer>();
			question.DispatchProtocolQuestionId = 0;
			question.DispatchProtocolId = protocolId;
			question.Answers = null;

			var savedQuestion = await _dispatchProtocolQuestionsRepository.SaveOrUpdateAsync(question, cancellationToken, true);

			foreach (var answer in answers)
			{
				answer.DispatchProtocolQuestionAnswerId = 0;
				answer.DispatchProtocolQuestionId = savedQuestion.DispatchProtocolQuestionId;
				await _dispatchProtocolQuestionAnswersRepository.SaveOrUpdateAsync(answer, cancellationToken, true);
			}

			question.Answers = answers;
		}

		public async Task<DispatchProtocol> UpdateProtocolAsync(DispatchProtocol edited, IEnumerable<int> removeAttachmentIds, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (edited == null || edited.DispatchProtocolId <= 0)
				throw new ArgumentException("An existing protocol is required.", nameof(edited));

			var current = await GetProtocolByIdAsync(edited.DispatchProtocolId);
			if (current == null || current.IsDeleted || (edited.DepartmentId > 0 && current.DepartmentId != edited.DepartmentId))
				throw new UnauthorizedAccessException("The protocol is not this department's, or it was deleted.");

			if (ProtocolRules.ForeignChildIds(current, edited).Count > 0)
				throw new UnauthorizedAccessException("The edit names triggers, questions or answers of another protocol.");

			var removeIds = new HashSet<int>(removeAttachmentIds ?? Enumerable.Empty<int>());
			if (removeIds.Any(id => current.Attachments.All(x => x.DispatchProtocolAttachmentId != id)))
				throw new UnauthorizedAccessException("The edit removes an attachment of another protocol.");

			// The protocol's own fields; its id, department, creator and creation time are never taken from the edit.
			current.Name = edited.Name?.Trim();
			current.Code = ProtocolRules.NormalizeCode(edited.Code);
			current.Description = StringHelpers.SanitizeHtmlForStorage(edited.Description);
			// The text is not edited here (P1): ProtocolText is the published version's, written only when a version is published.
			// Content changes go to the draft (IProtocolLibraryService.SaveDraftAsync).
			current.MinimumWeight = edited.MinimumWeight;
			current.IsDisabled = edited.IsDisabled;
			current.UpdatedOn = DateTime.UtcNow;
			current.UpdatedByUserId = string.IsNullOrWhiteSpace(userId) ? current.UpdatedByUserId : userId;

			var triggers = current.Triggers.ToList();
			var questions = current.Questions.ToList();
			var attachments = current.Attachments.ToList();

			await _dispatchProtocolRepository.SaveOrUpdateAsync(current, cancellationToken, true);

			// Triggers by id: changed in place, added, or removed when the edit no longer has them.
			var editedTriggers = edited.Triggers?.ToList() ?? new List<DispatchProtocolTrigger>();
			foreach (var stale in triggers.Where(x => editedTriggers.All(e => e.DispatchProtocolTriggerId != x.DispatchProtocolTriggerId)).ToList())
			{
				await _dispatchProtocolTriggersRepository.DeleteAsync(stale, cancellationToken);
				triggers.Remove(stale);
			}

			foreach (var trigger in editedTriggers)
			{
				var target = trigger.DispatchProtocolTriggerId > 0 ? triggers.First(x => x.DispatchProtocolTriggerId == trigger.DispatchProtocolTriggerId) : new DispatchProtocolTrigger();
				target.DispatchProtocolId = current.DispatchProtocolId;
				target.Type = trigger.Type;
				target.StartsOn = trigger.StartsOn;
				target.EndsOn = trigger.EndsOn;
				target.Priority = trigger.Priority;
				target.CallType = trigger.CallType;
				target.CallTypeId = trigger.CallTypeId;
				// P4 conditions (shared trigger engine).
				target.GeofenceGroupId = trigger.GeofenceGroupId;
				target.DaysOfWeek = trigger.DaysOfWeek;
				target.StartMinute = trigger.StartMinute;
				target.EndMinute = trigger.EndMinute;
				target.Keywords = trigger.Keywords;
				target.PoiTypeId = trigger.PoiTypeId;
				target.CallSources = trigger.CallSources;

				var saved = await _dispatchProtocolTriggersRepository.SaveOrUpdateAsync(target, cancellationToken, true);
				if (trigger.DispatchProtocolTriggerId <= 0)
					triggers.Add(saved);
			}

			// Questions and their answers by id, the same way. A removed question takes its answers with it.
			var editedQuestions = edited.Questions?.ToList() ?? new List<DispatchProtocolQuestion>();
			foreach (var stale in questions.Where(x => editedQuestions.All(e => e.DispatchProtocolQuestionId != x.DispatchProtocolQuestionId)).ToList())
			{
				foreach (var answer in stale.Answers ?? Enumerable.Empty<DispatchProtocolQuestionAnswer>())
					await _dispatchProtocolQuestionAnswersRepository.DeleteAsync(answer, cancellationToken);

				await _dispatchProtocolQuestionsRepository.DeleteAsync(stale, cancellationToken);
				questions.Remove(stale);
			}

			foreach (var question in editedQuestions)
			{
				if (question.DispatchProtocolQuestionId <= 0)
				{
					var added = new DispatchProtocolQuestion { Question = question.Question, Answers = question.Answers?.Select(a => new DispatchProtocolQuestionAnswer { Answer = a.Answer, Weight = a.Weight }).ToList() };
					await InsertQuestionAsync(current.DispatchProtocolId, added, cancellationToken);
					questions.Add(added);
					continue;
				}

				var target = questions.First(x => x.DispatchProtocolQuestionId == question.DispatchProtocolQuestionId);
				var answers = target.Answers?.ToList() ?? new List<DispatchProtocolQuestionAnswer>();
				target.Question = question.Question;
				target.Answers = null;
				await _dispatchProtocolQuestionsRepository.SaveOrUpdateAsync(target, cancellationToken, true);

				var editedAnswers = question.Answers?.ToList() ?? new List<DispatchProtocolQuestionAnswer>();
				foreach (var stale in answers.Where(x => editedAnswers.All(e => e.DispatchProtocolQuestionAnswerId != x.DispatchProtocolQuestionAnswerId)).ToList())
				{
					await _dispatchProtocolQuestionAnswersRepository.DeleteAsync(stale, cancellationToken);
					answers.Remove(stale);
				}

				foreach (var answer in editedAnswers)
				{
					var answerTarget = answer.DispatchProtocolQuestionAnswerId > 0 ? answers.First(x => x.DispatchProtocolQuestionAnswerId == answer.DispatchProtocolQuestionAnswerId) : new DispatchProtocolQuestionAnswer();
					answerTarget.DispatchProtocolQuestionId = target.DispatchProtocolQuestionId;
					answerTarget.Answer = answer.Answer;
					answerTarget.Weight = answer.Weight;

					var saved = await _dispatchProtocolQuestionAnswersRepository.SaveOrUpdateAsync(answerTarget, cancellationToken, true);
					if (answer.DispatchProtocolQuestionAnswerId <= 0)
						answers.Add(saved);
				}

				target.Answers = answers;
			}

			// Attachments: the ones asked to go, then the new files.
			foreach (var stale in attachments.Where(x => removeIds.Contains(x.DispatchProtocolAttachmentId)).ToList())
			{
				await _dispatchProtocolAttachmentRepository.DeleteAsync(stale, cancellationToken);
				attachments.Remove(stale);
			}

			foreach (var attachment in (edited.Attachments ?? Enumerable.Empty<DispatchProtocolAttachment>()).Where(x => x.DispatchProtocolAttachmentId <= 0 && x.Data != null))
			{
				attachment.DispatchProtocolId = current.DispatchProtocolId;
				var saved = await _dispatchProtocolAttachmentRepository.SaveOrUpdateAsync(attachment, cancellationToken, true);
				attachments.Add(new DispatchProtocolAttachment
				{
					DispatchProtocolAttachmentId = saved.DispatchProtocolAttachmentId,
					DispatchProtocolId = saved.DispatchProtocolId,
					FileName = saved.FileName,
					FileType = saved.FileType,
					Size = saved.Data?.LongLength
				});
			}

			current.Triggers = triggers;
			current.Questions = questions;
			current.Attachments = attachments;

			await AfterChangeAsync(current, cancellationToken);
			return current;
		}

		public async Task<DispatchProtocol> SetProtocolDisabledAsync(int protocolId, bool disabled, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var protocol = await GetProtocolHeaderByIdAsync(protocolId);

			if (protocol == null || protocol.IsDeleted)
				return null;

			if (protocol.IsDisabled != disabled)
			{
				protocol.IsDisabled = disabled;
				protocol.UpdatedOn = DateTime.UtcNow;
				if (!string.IsNullOrWhiteSpace(userId))
					protocol.UpdatedByUserId = userId;

				await _dispatchProtocolRepository.SaveOrUpdateAsync(protocol, cancellationToken, true);
				await AfterChangeAsync(protocol, cancellationToken);
			}

			return protocol;
		}

		public async Task<bool> DeleteProtocol(int id, CancellationToken cancellationToken = default(CancellationToken))
		{
			// A soft delete: calls that used the protocol keep citing it (the CallProtocols foreign key no longer cascades, so
			// removing the row would fail for any protocol that was ever used, and before M0333 it erased the protocol from
			// those calls). The row alone is read and written; its questions, triggers and attachments stay as they are.
			var protocol = await _dispatchProtocolRepository.GetDispatchProtocolByIdAsync(id);

			if (protocol == null)
				return false;

			if (!protocol.IsDeleted)
			{
				protocol.IsDeleted = true;
				protocol.DeletedOn = DateTime.UtcNow;
				await _dispatchProtocolRepository.SaveOrUpdateAsync(protocol, cancellationToken, true);
			}

			await InvalidateProtocolCachesAsync(protocol.DepartmentId);

			if (_searchProjections != null)
				await _searchProjections.Value.RemoveAsync(protocol.DepartmentId, SearchEntityTypes.Protocol, protocol.DispatchProtocolId.ToString(), cancellationToken);

			return true;
		}

		private async Task AfterChangeAsync(DispatchProtocol protocol, CancellationToken cancellationToken)
		{
			await InvalidateProtocolCachesAsync(protocol.DepartmentId);

			if (_searchProjections != null)
				await _searchProjections.Value.ProjectProtocolAsync(protocol, cancellationToken);
		}

		public async Task InvalidateProtocolCachesAsync(int departmentId)
		{
			if (_cacheProvider == null || departmentId <= 0)
				return;

			try
			{
				await _cacheProvider.RemoveAsync(string.Format(MatchingSetCacheKey, departmentId));
				await _cacheProvider.RemoveAsync(SyncService.ReferenceCacheKey(departmentId));
			}
			catch (Exception ex)
			{
				// The change is saved; a stale cache expires on its own (a day for the matching set, minutes for the reference).
				Logging.LogException(ex, $"Protocol caches for department {departmentId} could not be cleared.");
			}
		}

		#endregion Authoring
	}
}
