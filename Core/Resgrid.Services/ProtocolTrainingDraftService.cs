using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>
	/// "Draft a training from this protocol" (round 7A), Enhanced AI only: a baseline Training (never an LMS course) drafted from a published
	/// protocol's version in force. In order:
	/// 1. the gate: K1's knowledge entitlement (fails closed), the protocol author or approver right, the right to create trainings, the
	///    operator's model; "no AI" and licensed protocols are refused (decision 14) and Advanced Data Protection keeps content local;
	/// 2. the model drafts parts (each citing the sections it teaches: a title, a short introduction and key points) and single-choice
	///    questions with explanations; the output is validated (K2/6F rules: citations required, numbers only as the source wrote them; nothing
	///    valid refuses the draft) and stored only in the encrypted AiGenerations row;
	/// 3. the author reviews the draft and accepts it: the draft is read back from that row (never from the client), checked against the version
	///    in force again (same version, every cited section unchanged), and the training is created with the cited sections' own text after each
	///    part and a citation line to the version and anchor, unassigned and not open to members (a draft the author finishes and assigns, or
	///    uses for the protocol's free quiz-gated acknowledgment).
	/// </summary>
	public class ProtocolTrainingDraftService : IProtocolTrainingDraftService
	{
		public const string Feature = "ProtocolTrainingDraft";
		public const int ReservedTokens = 24000;
		public const int MinimumContentCharacters = 80;
		public const double DefaultPassMark = 80;

		private readonly IAiKnowledgeService _knowledge;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolLibraryService _library;
		private readonly IKnowledgePageTextSource _pages;
		private readonly IAuthorizationService _authorization;
		private readonly ITrainingService _trainings;
		private readonly IAdminAssistConversationProtection _conversationProtection;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly IKnowledgeGapsRepository _generations;
		private readonly IEventAggregator _eventAggregator;
		private readonly TimeProvider _clock;

		public ProtocolTrainingDraftService(IAiKnowledgeService knowledge, IDepartmentDataProtectionService protection, IProtocolAccessService access, IProtocolLibraryService library,
			IKnowledgePageTextSource pages, IAuthorizationService authorization, ITrainingService trainings, IAdminAssistConversationProtection conversationProtection,
			IAiBackgroundAdmission admission, IAiUsageMeter usage, Lazy<ILlmClient> client, IKnowledgeGapsRepository generations, IEventAggregator eventAggregator, TimeProvider clock)
		{
			_knowledge = knowledge;
			_protection = protection;
			_access = access;
			_library = library;
			_pages = pages;
			_authorization = authorization;
			_trainings = trainings;
			_conversationProtection = conversationProtection;
			_admission = admission;
			_usage = usage;
			_client = client;
			_generations = generations;
			_eventAggregator = eventAggregator;
			_clock = clock ?? TimeProvider.System;
		}

		#region Gate

		private async Task<bool> CanCreateTrainingsAsync(int departmentId, string userId)
		{
			try
			{
				return (await _authorization.GetTrainingManagementScopeAsync(userId, departmentId))?.CanCreate == true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The training author right could not be read for department {departmentId}.");
				return false;
			}
		}

		/// <summary>The protocol and its version in force as a source, or the refusal (cheapest first).</summary>
		private async Task<(DispatchProtocol Protocol, ProtocolAiSource Source, string Refusal)> GateAsync(int departmentId, string userId, int protocolId, CancellationToken cancellationToken)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || protocolId <= 0)
				return (null, null, ProtocolAiOutcomes.Invalid);
			if (!await ProtocolAiGate.EntitledAsync(_knowledge, departmentId))
				return (null, null, ProtocolAiOutcomes.NotEntitled);
			var (protocol, _, refusal) = await ProtocolAiGate.AuthorableAsync(_access, departmentId, userId, protocolId);
			if (refusal != null)
				return (null, null, refusal);
			if (!await CanCreateTrainingsAsync(departmentId, userId))
				return (null, null, ProtocolAiOutcomes.NotAllowed);
			if (!ProtocolLicensing.CanUseForAi(protocol))
				return (protocol, null, ProtocolAiOutcomes.Excluded);
			var version = await ProtocolAiGate.VersionInForceAsync(_library, departmentId, protocol);
			if (version == null)
				return (protocol, null, ProtocolAiOutcomes.NotFound);
			return (protocol, await ProtocolAiSources.FromVersionAsync(_pages, departmentId, protocol, version, cancellationToken), null);
		}

		public async Task<ProtocolAiStatus> GetStatusAsync(int departmentId, string userId, int protocolId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || protocolId <= 0)
				return new ProtocolAiStatus { Reason = ProtocolAiOutcomes.Invalid };
			if (!await ProtocolAiGate.EntitledAsync(_knowledge, departmentId))
				return new ProtocolAiStatus { Reason = ProtocolAiOutcomes.NotEntitled };
			var (protocol, _, refusal) = await ProtocolAiGate.AuthorableAsync(_access, departmentId, userId, protocolId);
			if (refusal != null)
				return new ProtocolAiStatus { Reason = refusal };
			if (!await CanCreateTrainingsAsync(departmentId, userId))
				return new ProtocolAiStatus { Reason = ProtocolAiOutcomes.NotAllowed };
			if (!ProtocolLicensing.CanUseForAi(protocol))
				return new ProtocolAiStatus { Reason = ProtocolAiOutcomes.Excluded };
			if (await ProtocolAiGate.VersionInForceAsync(_library, departmentId, protocol) == null)
				return new ProtocolAiStatus { Reason = ProtocolAiOutcomes.NotFound };
			if (!AiKnowledgeService.OperatorConfigured())
				return new ProtocolAiStatus { Reason = ProtocolAiOutcomes.Unconfigured };
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, departmentId))
				return new ProtocolAiStatus { Reason = ProtocolAiOutcomes.KeptLocal };
			return new ProtocolAiStatus { Available = true };
		}

		#endregion Gate

		#region Draft

		/// <summary>What is kept (encrypted, in AiGenerations) to create the training from: the version, the validated draft and each section's hash.</summary>
		private sealed class StoredTraining
		{
			public int P { get; set; }
			public int V { get; set; }
			public string T { get; set; }
			public string S { get; set; }
			public List<StoredPart> Parts { get; set; } = new List<StoredPart>();
			public List<StoredQuestion> Q { get; set; } = new List<StoredQuestion>();
			public List<StoredSection> Src { get; set; } = new List<StoredSection>();
		}

		private sealed class StoredPart
		{
			public string T { get; set; }
			public string I { get; set; }
			public List<string> K { get; set; } = new List<string>();
			public List<string> A { get; set; } = new List<string>();
		}

		private sealed class StoredQuestion
		{
			public string T { get; set; }
			public List<string> O { get; set; } = new List<string>();
			public int C { get; set; }
			public string E { get; set; }
			public List<string> A { get; set; } = new List<string>();
		}

		/// <summary>A cited anchor and the hash of its text when drafted.</summary>
		private sealed class StoredSection
		{
			public string A { get; set; }
			public string H { get; set; }
		}

		private static ProtocolTrainingDraft Refused(string outcome) => new ProtocolTrainingDraft { Outcome = outcome, PromptVersion = AiProtocolStudioPrompt.TrainingVersion };

		public async Task<ProtocolTrainingDraft> DraftAsync(ProtocolTrainingDraftRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null)
				return Refused(ProtocolAiOutcomes.Invalid);
			var (protocol, source, refusal) = await GateAsync(request.DepartmentId, request.UserId, request.ProtocolId, cancellationToken);
			if (refusal != null)
				return Refused(refusal);
			if (!AiKnowledgeService.OperatorConfigured())
				return Refused(ProtocolAiOutcomes.Unconfigured);
			if (source.Sections.Sum(s => s.Text.Length) < MinimumContentCharacters)
				return Refused(ProtocolAiOutcomes.NoContent);
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, request.DepartmentId))
				return Refused(ProtocolAiOutcomes.KeptLocal);

			var aiActor = new AdminAssistActor(request.DepartmentId, request.UserId, request.Locale ?? "en");
			var admission = await KnowledgeModelCalls.ReserveAsync(_usage, _admission, _clock, request.DepartmentId, Feature, ReservedTokens, cancellationToken);
			if (admission.Outcome != null)
				return Refused(admission.Outcome);

			var used = 0;
			var ledger = "Unavailable";
			var draft = Refused(ProtocolAiOutcomes.Unavailable);
			draft.SourceTitle = source.Title;
			draft.SourceVersionLabel = source.VersionLabel;
			try
			{
				try
				{
					// Fail closed: an exchange that cannot be audited is never sent to the model.
					await _conversationProtection.PreflightAsync(aiActor, cancellationToken);
				}
				catch (UnauthorizedAccessException)
				{
					draft.Outcome = ProtocolAiOutcomes.Unconfigured;
					return draft;
				}

				var numbered = AiCourseStudioPrompt.Number(source.Sections.Select(s => (source.Title, (string)source.VersionLabel, (string)s.HeadingPath, s.Text)).ToList(), out var truncated);
				var questionCount = Math.Clamp(request.QuestionCount, ProtocolTrainingDraftRequest.MinQuestions, ProtocolTrainingDraftRequest.MaxQuestions);
				LlmResult completion;
				using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(KnowledgeConfig.AnswerTimeoutSeconds * 4, 20, 300)));
					try
					{
						completion = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, AiProtocolStudioPrompt.TrainingMessages(numbered, questionCount),
							new[] { AiProtocolStudioPrompt.TrainingToolDefinition() }, Math.Clamp(KnowledgeConfig.AnswerMaxOutputTokens * 4, 2048, 8192),
							AiCacheIsolation.SaltFor(request.DepartmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
					}
					catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
					{
						return draft;
					}
				}

				used = Math.Min(admission.Reservation.Tokens, Math.Max(0, completion.InputTokens + completion.OutputTokens));
				AiTrainingDraft validated;
				try
				{
					using var document = AiProtocolStudioPrompt.Parse(completion, AiProtocolStudioPrompt.TrainingTool);
					validated = AiProtocolStudioPrompt.ValidateTraining(document, numbered);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
				{
					// Nothing the model wrote is shown.
					ledger = "InvalidOutput";
					draft.Outcome = ProtocolAiOutcomes.Refused;
					draft.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, aiActor, AiProtocolStudioPrompt.TrainingVersion,
						"protocol-training protocol:" + protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture), Evidence(source), ProtocolAiOutcomes.Refused,
						completion.InputTokens, completion.OutputTokens, cancellationToken);
					return draft;
				}

				ledger = "Answered";
				var sectionByRef = numbered.Select((s, i) => (s.Ref, Section: source.Sections[i])).ToDictionary(x => x.Ref, x => x.Section, StringComparer.Ordinal);
				List<string> Anchors(IEnumerable<string> refs) => refs.Where(sectionByRef.ContainsKey).Select(r => sectionByRef[r].Anchor ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
				var stored = new StoredTraining
				{
					P = protocol.DispatchProtocolId,
					V = source.VersionId,
					T = validated.Title,
					S = validated.Summary,
					Parts = validated.Sections.Select(p => new StoredPart { T = p.Title, I = p.Intro, K = p.KeyPoints.ToList(), A = Anchors(p.Refs) }).ToList(),
					Q = validated.Questions.Select(q => new StoredQuestion { T = q.Text, O = q.Options.ToList(), C = q.CorrectIndex, E = q.Explanation, A = Anchors(q.Refs) }).ToList()
				};
				stored.Src = stored.Parts.SelectMany(p => p.A).Concat(stored.Q.SelectMany(q => q.A)).Distinct(StringComparer.Ordinal)
					.Select(a => new StoredSection { A = a, H = KnowledgeChunker.Hash(source.TextOf(a)) }).ToList();

				draft.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, aiActor, AiProtocolStudioPrompt.TrainingVersion,
					System.Text.Json.JsonSerializer.Serialize(stored), Evidence(source), ProtocolAiOutcomes.Drafted, completion.InputTokens, completion.OutputTokens, cancellationToken);
				if (draft.ExchangeId == null)
				{
					// A draft that cannot be read back cannot be accepted: it is not offered.
					draft.Outcome = ProtocolAiOutcomes.Unavailable;
					return draft;
				}

				draft.Outcome = ProtocolAiOutcomes.Drafted;
				draft.Dropped = validated.Dropped;
				draft.Truncated = truncated;
				Fill(draft, stored, source);
				return draft;
			}
			finally
			{
				await KnowledgeModelCalls.CompleteAsync(_usage, admission.Reservation, used, ledger);
			}
		}

		private static void Fill(ProtocolTrainingDraft draft, StoredTraining stored, ProtocolAiSource source)
		{
			draft.Title = string.IsNullOrWhiteSpace(stored.T) ? source.Title : stored.T;
			draft.Summary = stored.S;
			draft.Sections = stored.Parts.Select(p => new ProtocolTrainingDraftSection { Title = p.T, Intro = p.I, KeyPoints = p.K.ToList(), Citations = Citations(source, p.A) }).ToList();
			draft.Questions = stored.Q.Select(q => new ProtocolTrainingDraftQuestion
			{
				Text = q.T,
				Options = q.O.ToList(),
				CorrectIndex = q.C,
				Explanation = q.E,
				Citations = Citations(source, q.A)
			}).ToList();
		}

		private static List<ProtocolAiCitation> Citations(ProtocolAiSource source, IEnumerable<string> anchors) =>
			anchors.Select(a => (Anchor: a, Section: source.Sections.FirstOrDefault(s => string.Equals(s.Anchor ?? string.Empty, a ?? string.Empty, StringComparison.Ordinal))))
				.Where(x => x.Section != null)
				.Select(x => new ProtocolAiCitation { Anchor = x.Anchor, Label = source.Label(x.Section), Url = source.Url(x.Anchor) })
				.ToList();

		private static IEnumerable<string> Evidence(ProtocolAiSource source)
		{
			var item = "protocol:" + source.ProtocolId.ToString(CultureInfo.InvariantCulture) + ":" + source.VersionId.ToString(CultureInfo.InvariantCulture);
			return source.Sections.Select(s => item + "#" + (s.Anchor ?? string.Empty)).Distinct(StringComparer.Ordinal).Take(50);
		}

		#endregion Draft

		#region Create

		public async Task<ProtocolTrainingDraftResult> CreateAsync(int departmentId, string userId, int protocolId, string exchangeId, string title, string locale,
			CancellationToken cancellationToken = default)
		{
			if (!Guid.TryParseExact(exchangeId ?? string.Empty, "D", out _))
				return ProtocolTrainingDraftResult.Fail(ProtocolAiOutcomes.Invalid);
			var (protocol, source, refusal) = await GateAsync(departmentId, userId, protocolId, cancellationToken);
			if (refusal != null)
				return ProtocolTrainingDraftResult.Fail(refusal);

			// The author's own draft, as stored: never what the client sends back.
			var row = await _generations.GetGenerationAsync(departmentId, userId, exchangeId, cancellationToken);
			if (row == null || !string.Equals(row.PromptVersion, AiProtocolStudioPrompt.TrainingVersion, StringComparison.Ordinal) ||
				!string.Equals(row.Outcome, ProtocolAiOutcomes.Drafted, StringComparison.Ordinal))
				return ProtocolTrainingDraftResult.Fail(ProtocolAiOutcomes.NotFound);

			StoredTraining stored;
			try
			{
				var content = await _conversationProtection.ReadAsync(new AdminAssistActor(departmentId, userId, "en"), row, cancellationToken);
				stored = string.IsNullOrWhiteSpace(content?.Question) ? null : System.Text.Json.JsonSerializer.Deserialize<StoredTraining>(content.Question);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, "A protocol training draft could not be read back.");
				stored = null;
			}
			if (stored == null || stored.P != protocol.DispatchProtocolId || stored.Parts == null || stored.Parts.Count == 0)
				return ProtocolTrainingDraftResult.Fail(ProtocolAiOutcomes.NotFound);

			// Still the version in force, every cited section unchanged and still holding the numbers the draft shows.
			if (source.VersionId != stored.V)
				return ProtocolTrainingDraftResult.Fail(ProtocolAiOutcomes.Stale);
			foreach (var section in stored.Src ?? new List<StoredSection>())
				if (!source.HasAnchor(section.A) || !string.Equals(KnowledgeChunker.Hash(source.TextOf(section.A)), section.H, StringComparison.Ordinal))
					return ProtocolTrainingDraftResult.Fail(ProtocolAiOutcomes.Stale);
			foreach (var part in stored.Parts)
				if (part.A == null || part.A.Count == 0 || !AiCourseStudioPrompt.Quoted(part.T + "\n" + part.I + "\n" + string.Join("\n", part.K ?? new List<string>()), part.A.Select(source.TextOf)))
					return ProtocolTrainingDraftResult.Fail(ProtocolAiOutcomes.Refused);
			var questions = (stored.Q ?? new List<StoredQuestion>()).Where(q => q.A != null && q.A.Count > 0 && q.O != null && q.C >= 0 && q.C < q.O.Count &&
				AiCourseStudioPrompt.Quoted(q.T + "\n" + string.Join("\n", q.O) + "\n" + q.E, q.A.Select(source.TextOf))).ToList();

			var training = Build(departmentId, userId, protocol, source, stored, questions, title, string.IsNullOrWhiteSpace(locale) ? "en" : locale, _clock.GetUtcNow().UtcDateTime);
			if (TrainingQuizScorer.Validate(training).Count > 0)
				return ProtocolTrainingDraftResult.Fail(ProtocolAiOutcomes.NotSaved);

			Training saved;
			try
			{
				saved = await _trainings.SaveAsync(training, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"A training drafted from protocol {protocol.DispatchProtocolId} could not be saved for department {departmentId}.");
				return ProtocolTrainingDraftResult.Fail(ProtocolAiOutcomes.NotSaved);
			}
			if (saved == null || saved.TrainingId <= 0)
				return ProtocolTrainingDraftResult.Fail(ProtocolAiOutcomes.NotSaved);

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.ProtocolTrainingDraftedWithAi,
				After = Newtonsoft.Json.JsonConvert.SerializeObject(new
				{
					saved.TrainingId,
					protocol.DispatchProtocolId,
					ProtocolVersionId = source.VersionId,
					questions = questions.Count,
					exchangeId
				}),
				Successful = true,
				ServerName = Environment.MachineName
			});
			return new ProtocolTrainingDraftResult { Outcome = ProtocolAiOutcomes.Created, TrainingId = saved.TrainingId, Questions = questions.Count };
		}

		/// <summary>
		/// The training as created: no assignees and not open to members (a draft the author assigns), the drafted parts with the cited sections'
		/// own text and a citation line linking the version and anchor, and the quiz (single choice, one correct option; the pass mark is the
		/// training default when there is a quiz). Doses and numbers appear only as the protocol wrote them.
		/// </summary>
		public static Training BuildTraining(int departmentId, string userId, DispatchProtocol protocol, ProtocolAiSource source, string title, string summary,
			IReadOnlyList<(string Title, string Intro, IReadOnlyList<string> KeyPoints, IReadOnlyList<string> Anchors)> parts,
			IReadOnlyList<(string Text, IReadOnlyList<string> Options, int Correct)> questions, string language, DateTime utcNow)
		{
			var sourceLabel = string.IsNullOrWhiteSpace(source.VersionLabel) ? source.Title : source.Title + " " + source.VersionLabel;
			var name = !string.IsNullOrWhiteSpace(title) ? title.Trim() : source.Title;
			if (name.Length > 200)
				name = name.Substring(0, 200);

			var description = new StringBuilder();
			if (!string.IsNullOrWhiteSpace(summary))
				description.Append("<p>").Append(WebUtility.HtmlEncode(summary)).Append("</p>");
			description.Append("<p><em>").Append(WebUtility.HtmlEncode(string.Format(CultureInfo.InvariantCulture, ProtocolAiText.Get("TrainingDraftNote", language), sourceLabel))).Append("</em></p>");

			var text = new StringBuilder();
			foreach (var part in parts)
			{
				text.Append("<h2>").Append(WebUtility.HtmlEncode(part.Title)).Append("</h2>");
				if (!string.IsNullOrWhiteSpace(part.Intro))
					text.Append("<p>").Append(WebUtility.HtmlEncode(part.Intro)).Append("</p>");
				if (part.KeyPoints.Count > 0)
				{
					text.Append("<p><strong>").Append(WebUtility.HtmlEncode(ProtocolAiText.Get("TrainingKeyPoints", language))).Append("</strong></p><ul>");
					foreach (var point in part.KeyPoints)
						text.Append("<li>").Append(WebUtility.HtmlEncode(point)).Append("</li>");
					text.Append("</ul>");
				}

				// The cited sections' own text, per anchor in source order: the protocol's words, numbers and doses as written.
				var cited = part.Anchors.Where(source.HasAnchor).Distinct(StringComparer.Ordinal).ToList();
				foreach (var anchor in cited)
				{
					var heading = source.Sections.First(s => string.Equals(s.Anchor ?? string.Empty, anchor ?? string.Empty, StringComparison.Ordinal)).HeadingPath;
					text.Append("<h3>").Append(WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(heading) ? ProtocolAiText.Get("TrainingFromTheProtocol", language) : heading)).Append("</h3>");
					text.Append("<blockquote>").Append(ProtocolAiSources.Paragraphs(source.TextOf(anchor))).Append("</blockquote>");
				}

				text.Append("<p><em>").Append(WebUtility.HtmlEncode(ProtocolAiText.Get("TrainingCitedLabel", language))).Append(' ');
				text.Append(string.Join("; ", cited.Select(anchor =>
				{
					var section = source.Sections.First(s => string.Equals(s.Anchor ?? string.Empty, anchor ?? string.Empty, StringComparison.Ordinal));
					var label = WebUtility.HtmlEncode(source.Label(section));
					// Stored training html keeps only absolute links (the sanitizer drops relative ones): the system's own address in front.
					var url = ProtocolAiSources.AbsoluteUrl(source.Url(anchor));
					return string.IsNullOrWhiteSpace(url) ? label : "<a href=\"" + WebUtility.HtmlEncode(url) + "\">" + label + "</a>";
				}))).Append("</em></p>");
			}

			return new Training
			{
				DepartmentId = departmentId,
				CreatedByUserId = userId,
				CreatedOn = utcNow,
				Name = name,
				Description = description.ToString(),
				TrainingText = text.ToString(),
				MinimumScore = questions.Count > 0 ? DefaultPassMark : 0,
				// A draft: nobody is assigned, members do not see it, and nothing is announced until the author assigns it.
				OpenToAllMembers = false,
				AllowSelfEnroll = false,
				SendOverdueNotices = true,
				Questions = questions.Select(q => new TrainingQuestion
				{
					Question = q.Text,
					Type = (int)TrainingQuestionTypes.SingleChoice,
					Answers = q.Options.Select((o, i) => new TrainingQuestionAnswer { Answer = o, Correct = i == q.Correct }).ToList()
				}).ToList(),
				Users = new List<TrainingUser>(),
				Audiences = new List<TrainingAudience>()
			};
		}

		private static Training Build(int departmentId, string userId, DispatchProtocol protocol, ProtocolAiSource source, StoredTraining stored, List<StoredQuestion> questions,
			string title, string language, DateTime utcNow) =>
			BuildTraining(departmentId, userId, protocol, source, !string.IsNullOrWhiteSpace(title) ? title : string.IsNullOrWhiteSpace(stored.T) ? source.Title : stored.T, stored.S,
				stored.Parts.Select(p => (p.T, p.I, (IReadOnlyList<string>)(p.K ?? new List<string>()), (IReadOnlyList<string>)p.A)).ToList(),
				questions.Select(q => (q.T, (IReadOnlyList<string>)q.O, q.C)).ToList(), language, utcNow);

		#endregion Create
	}
}
