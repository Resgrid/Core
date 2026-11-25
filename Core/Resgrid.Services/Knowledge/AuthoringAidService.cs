using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Knowledge
{
	/// <summary>
	/// The K2 authoring aids (protocols-training-knowledge-plan §16.4, §24 K2): draft quiz questions with explanations, a course outline, or
	/// a change summary from a version diff, for an author to review. In order:
	/// 1. the gate is K1's (<see cref="IAiKnowledgeService.IsEntitledAsync"/>: Enhanced AI with Ai.Knowledge, self-hosted listing, fails
	///    closed) and the operator's model must be configured;
	/// 2. the source for the content kind decides authorship, the "no AI" flag (decision 14) and the grounding text (a change summary gets
	///    the diff only);
	/// 3. Advanced Data Protection in any state but Disabled keeps department content local: no model call;
	/// 4. the shared inference slot and monthly budget are reserved, the exchange preflighted for audit (fails closed), the prompt run, and
	///    the output validated item by item (K1's number-in-source rule: an item with a number the grounding lacks is left out).
	/// Each draft is logged to AiGenerations. Nothing is saved to the content: the editor saves what the author accepts.
	/// </summary>
	public class AuthoringAidService : IAuthoringAidService
	{
		public const string Feature = "AuthoringAids";
		public const int ReservedTokens = 8192;
		public const int MaxAdmissionWaitSeconds = 8;
		public const int MinimumContentCharacters = 80;

		private readonly IAiKnowledgeService _knowledge;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IEnumerable<IAuthoringAidSource> _sources;
		private readonly IAdminAssistConversationProtection _conversationProtection;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly IKnowledgeGapsRepository _generations;
		private readonly TimeProvider _clock;

		public AuthoringAidService(IAiKnowledgeService knowledge, IDepartmentsService departments, IDepartmentDataProtectionService protection,
			IEnumerable<IAuthoringAidSource> sources, IAdminAssistConversationProtection conversationProtection, IAiBackgroundAdmission admission, IAiUsageMeter usage,
			Lazy<ILlmClient> client, IKnowledgeGapsRepository generations, TimeProvider clock)
		{
			_knowledge = knowledge;
			_departments = departments;
			_protection = protection;
			_sources = sources ?? Enumerable.Empty<IAuthoringAidSource>();
			_conversationProtection = conversationProtection;
			_admission = admission;
			_usage = usage;
			_client = client;
			_generations = generations;
			_clock = clock ?? TimeProvider.System;
		}

		public async Task<AuthoringAidStatus> GetStatusAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || !await _knowledge.IsEntitledAsync(departmentId))
				return new AuthoringAidStatus { Reason = AuthoringAidOutcomes.NotEntitled };
			if (!await IsMemberAsync(departmentId, userId))
				return new AuthoringAidStatus { Reason = AuthoringAidOutcomes.NotEntitled };
			if (!AiKnowledgeService.OperatorConfigured())
				return new AuthoringAidStatus { Reason = AuthoringAidOutcomes.Unconfigured };
			if (await IsProtectedAsync(departmentId))
				return new AuthoringAidStatus { Reason = AuthoringAidOutcomes.KeptLocal };
			return new AuthoringAidStatus { Available = true };
		}

		private async Task<bool> IsMemberAsync(int departmentId, string userId)
		{
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null || department.DepartmentId != departmentId)
				return false;
			return string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase) ||
				DepartmentMemberStateHelper.IsCurrentMember(await _departments.GetDepartmentMemberAsync(userId, departmentId, false), departmentId);
		}

		/// <summary>Any Advanced Data Protection state but Disabled keeps content local; an unreadable state does too.</summary>
		private async Task<bool> IsProtectedAsync(int departmentId)
		{
			try
			{
				return await _protection.GetStateAsync(departmentId, true) != DepartmentDataProtectionState.Disabled;
			}
			catch (Exception)
			{
				return true;
			}
		}

		private static AuthoringAidDraft Refused(string outcome, int kind) => new AuthoringAidDraft
		{
			Outcome = outcome,
			Kind = kind,
			PromptVersion = Enum.IsDefined(typeof(AuthoringAidKinds), kind) ? AiAuthoringPrompt.VersionFor(kind) : null
		};

		public async Task<AuthoringAidDraft> DraftAsync(AuthoringAidRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.UserId) ||
				!Enum.IsDefined(typeof(AuthoringAidKinds), request.Kind) || !Enum.IsDefined(typeof(AuthoringAidSourceKinds), request.SourceKind))
				return Refused(AuthoringAidOutcomes.Invalid, request?.Kind ?? 0);
			if (request.ContentHtml != null && request.ContentHtml.Length > AuthoringAidRequest.ContentMaxLength)
				return Refused(AuthoringAidOutcomes.Invalid, request.Kind);

			var kind = request.Kind;
			if (!await _knowledge.IsEntitledAsync(request.DepartmentId) || !await IsMemberAsync(request.DepartmentId, request.UserId))
				return Refused(AuthoringAidOutcomes.NotEntitled, kind);
			if (!AiKnowledgeService.OperatorConfigured())
				return Refused(AuthoringAidOutcomes.Unconfigured, kind);

			// A kind no source claims is never drafted from.
			var source = _sources.FirstOrDefault(s => (int)s.Kind == request.SourceKind);
			if (source == null)
				return Refused(AuthoringAidOutcomes.NotFound, kind);

			AuthoringAidSourceResult loaded;
			try
			{
				loaded = await source.LoadAsync(request, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, "An authoring aid source could not be read.");
				return Refused(AuthoringAidOutcomes.Unavailable, kind);
			}

			if (loaded?.Source == null)
				return Refused(loaded?.Outcome ?? AuthoringAidOutcomes.NotFound, kind);
			var content = loaded.Source;
			if (content.ExcludedFromAi || request.ExcludeFromAi && request.SourceKind == (int)AuthoringAidSourceKinds.Training)
				return Refused(AuthoringAidOutcomes.Excluded, kind);

			// A change summary rests on the diff alone; quiz and outline on the content.
			var grounding = kind == (int)AuthoringAidKinds.ChangeSummary ? content.DiffText : content.Text;
			if (kind == (int)AuthoringAidKinds.ChangeSummary && string.IsNullOrWhiteSpace(grounding))
				return Refused(AuthoringAidOutcomes.NoChanges, kind);
			if (string.IsNullOrWhiteSpace(grounding) || kind != (int)AuthoringAidKinds.ChangeSummary && grounding.Trim().Length < MinimumContentCharacters)
				return Refused(AuthoringAidOutcomes.NoContent, kind);

			if (await IsProtectedAsync(request.DepartmentId))
				return Refused(AuthoringAidOutcomes.KeptLocal, kind);

			var actor = new AdminAssistActor(request.DepartmentId, request.UserId, request.Locale ?? "en");
			var admission = await ReserveAsync(request.DepartmentId, cancellationToken);
			if (admission.Outcome != null)
				return Refused(admission.Outcome, kind);

			var used = 0;
			var ledger = "Unavailable";
			var draft = Refused(AuthoringAidOutcomes.Unavailable, kind);
			draft.SourceTitle = content.Title;
			try
			{
				try
				{
					// Fail closed: a draft that cannot be audited is never requested.
					await _conversationProtection.PreflightAsync(actor, cancellationToken);
				}
				catch (UnauthorizedAccessException)
				{
					draft.Outcome = AuthoringAidOutcomes.Unconfigured;
					return draft;
				}

				var questionCount = Math.Clamp(request.QuestionCount <= 0 ? 5 : request.QuestionCount, AuthoringAidRequest.MinQuestions, AuthoringAidRequest.MaxQuestions);
				var messages = AiAuthoringPrompt.Messages(kind, content.Title, grounding, questionCount, content.PreviousVersionLabel, content.VersionLabel);
				LlmResult result;
				using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(KnowledgeConfig.AnswerTimeoutSeconds * 2, 10, 180)));
					try
					{
						result = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, messages, new[] { AiAuthoringPrompt.Tool(kind) },
							Math.Clamp(KnowledgeConfig.AnswerMaxOutputTokens * 2, 512, 4096),
							AiCacheIsolation.SaltFor(request.DepartmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
					}
					catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
					{
						return draft;
					}
				}

				used = Math.Min(admission.Reservation.Tokens, Math.Max(0, result.InputTokens + result.OutputTokens));
				AiAuthoringDraft validated;
				try
				{
					using var document = AiAuthoringPrompt.Parse(result, kind);
					validated = AiAuthoringPrompt.Validate(document, kind, grounding);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is JsonException)
				{
					// Nothing the model wrote is shown.
					ledger = "InvalidOutput";
					draft.Outcome = AuthoringAidOutcomes.Refused;
					draft.ExchangeId = await LogAsync(actor, request, content, AuthoringAidOutcomes.Refused, result.InputTokens, result.OutputTokens, cancellationToken);
					return draft;
				}

				ledger = "Answered";
				draft.Outcome = AuthoringAidOutcomes.Drafted;
				draft.Dropped = validated.Dropped;
				draft.Questions = validated.Questions.Select(q => new AuthoringAidQuestionDraft { Text = q.Text, Options = q.Options.ToList(), CorrectIndex = q.CorrectIndex, Explanation = q.Explanation }).ToList();
				draft.Outline = validated.Modules.Select(m => new AuthoringAidOutlineModule { Title = m.Title, Objective = m.Objective, Lessons = m.Lessons.ToList() }).ToList();
				draft.Changes = validated.Changes.ToList();
				draft.ExchangeId = await LogAsync(actor, request, content, AuthoringAidOutcomes.Drafted, result.InputTokens, result.OutputTokens, cancellationToken);
				return draft;
			}
			finally
			{
				// Failed inference is not charged: the GPU and its outages are the operator's, not the department's.
				try { await _usage.CompleteAsync(admission.Reservation, used, ledger, CancellationToken.None); }
				catch (Exception ex) { Logging.LogException(ex); }
			}
		}

		private sealed record Admission(AiUsageReservation Reservation, string Outcome);

		/// <summary>The shared inference slot and the department's monthly budget (the Enhanced AI admission), waited for briefly.</summary>
		private async Task<Admission> ReserveAsync(int departmentId, CancellationToken cancellationToken)
		{
			var tier = AiOperatorSettings.IsListedDepartment(AiConfig.SelfHostedDepartmentIds, departmentId) ? AdminAssistAskTiers.SelfHosted : AdminAssistAskTiers.EnhancedAi;
			var giveUp = _clock.GetUtcNow().UtcDateTime.AddSeconds(MaxAdmissionWaitSeconds);
			while (true)
			{
				var now = _clock.GetUtcNow().UtcDateTime;
				if (await _usage.RemainingAsync(departmentId, now, AiConfig.MonthlyTokenLimit, cancellationToken) < ReservedTokens)
					return new Admission(null, AuthoringAidOutcomes.BudgetExhausted);
				var reservation = await _admission.ReserveBackgroundAsync(departmentId, Feature, tier, now, ReservedTokens, AiConfig.MonthlyTokenLimit, cancellationToken);
				if (reservation != null)
					return new Admission(reservation, null);
				if (now >= giveUp)
					return new Admission(null, AuthoringAidOutcomes.Busy);
				await Task.Delay(TimeSpan.FromMilliseconds(500), _clock, cancellationToken);
			}
		}

		/// <summary>
		/// One AiGenerations row per draft, like K1: the aid and the cited item (never the content), encrypted by the conversation protection,
		/// with the prompt version, model revision and digest, tokens and outcome. A failure to log is reported and returns null.
		/// </summary>
		private async Task<string> LogAsync(AdminAssistActor actor, AuthoringAidRequest request, AuthoringAidSource content, string outcome, int inputTokens, int outputTokens,
			CancellationToken cancellationToken)
		{
			try
			{
				var id = Guid.NewGuid().ToString("D");
				var aid = "aid:" + ((AuthoringAidKinds)request.Kind).ToString().ToLowerInvariant();
				var item = content.ItemKey ?? (((AuthoringAidSourceKinds)request.SourceKind).ToString() + ":" + request.SourceId.ToString(CultureInfo.InvariantCulture));
				var row = new AiGenerationRow
				{
					Id = id,
					ConversationId = id,
					DepartmentId = actor.DepartmentId,
					UserId = actor.UserId,
					Revision = 1,
					CreatedOnUtc = _clock.GetUtcNow().UtcDateTime,
					Content = JsonSerializer.Serialize(new AskStoredContent(aid + " " + item, Array.Empty<AskToolInput>(), new[] { item })),
					PromptVersion = AiAuthoringPrompt.VersionFor(request.Kind),
					ModelRevision = AiConfig.ModelRevision ?? string.Empty,
					RuntimeDigest = AiConfig.RuntimeDigest ?? string.Empty,
					RequestDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.DepartmentId.ToString(CultureInfo.InvariantCulture) + ":" + actor.UserId + ":" + aid + ":" + item))).ToLowerInvariant(),
					InputTokens = Math.Max(0, inputTokens),
					OutputTokens = Math.Max(0, outputTokens),
					Outcome = outcome
				};
				await _conversationProtection.ProtectAsync(actor, row, cancellationToken);
				await _generations.InsertGenerationAsync(row, cancellationToken);
				return id;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"An authoring aid draft could not be logged for department {actor.DepartmentId}.");
				return null;
			}
		}
	}
}
