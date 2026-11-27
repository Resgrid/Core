using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>
	/// Course drafts from a protocol or SOP (T5 item 5, §13a "Learning Studio: SOP → course", §16.3). In order:
	/// 1. the gates, cheapest first, failing closed: the LMS add-on's authoring gate and Manage Courses (§17.2: course drafts need the LMS), then
	///    K1's knowledge entitlement (Enhanced AI with Ai.Knowledge) and the operator's model;
	/// 2. the source: a published protocol version the member may read and author that is not "no AI" or licensed (decision 14), or an uploaded
	///    SOP's extracted text (the file is never kept), cut into sections exactly as the knowledge index cuts it;
	/// 3. Advanced Data Protection in any state but Disabled keeps department content local: no model call;
	/// 4. the shared inference slot and budget, the exchange preflighted for audit (fails closed), the prompt run, and the output validated item by
	///    item: every lesson must cite shown sections and every number it holds must be in those sections; a question likewise;
	/// 5. the draft written as a NEW DRAFT course through the course services (never submitted, never published): each lesson is the model's short
	///    introduction and key points followed by the cited sections' own text and a citation line, so doses and numbers appear only as the source
	///    wrote them; the drafted questions go to a new question bank with a closing check; the provenance (source, version, citations, exchange)
	///    is recorded on the course. Each draft is logged to AiGenerations.
	/// </summary>
	public class LmsCourseDraftService : ILmsCourseDraftService
	{
		public const string Feature = "CourseDraft";
		public const int ReservedTokens = 24000;
		public const int MinimumContentCharacters = 80;

		private readonly IAiKnowledgeService _knowledge;
		private readonly ILmsGateService _gate;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IProtocolAccessService _protocolAccess;
		private readonly IProtocolLibraryService _library;
		private readonly IProtocolsService _protocols;
		private readonly IKnowledgePageTextSource _pages;
		private readonly IProtocolTextExtractor _extractor;
		private readonly ILmsCourseService _courses;
		private readonly ILmsQuestionBankService _banks;
		private readonly ILmsCourseOriginsRepository _origins;
		private readonly IAdminAssistConversationProtection _conversationProtection;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly IKnowledgeGapsRepository _generations;
		private readonly IEventAggregator _eventAggregator;
		private readonly TimeProvider _clock;

		public LmsCourseDraftService(IAiKnowledgeService knowledge, ILmsGateService gate, IDepartmentDataProtectionService protection, IProtocolAccessService protocolAccess,
			IProtocolLibraryService library, IProtocolsService protocols, IKnowledgePageTextSource pages, IProtocolTextExtractor extractor, ILmsCourseService courses,
			ILmsQuestionBankService banks, ILmsCourseOriginsRepository origins, IAdminAssistConversationProtection conversationProtection, IAiBackgroundAdmission admission,
			IAiUsageMeter usage, Lazy<ILlmClient> client, IKnowledgeGapsRepository generations, IEventAggregator eventAggregator, TimeProvider clock)
		{
			_knowledge = knowledge;
			_gate = gate;
			_protection = protection;
			_protocolAccess = protocolAccess;
			_library = library;
			_protocols = protocols;
			_pages = pages;
			_extractor = extractor;
			_courses = courses;
			_banks = banks;
			_origins = origins;
			_conversationProtection = conversationProtection;
			_admission = admission;
			_usage = usage;
			_client = client;
			_generations = generations;
			_eventAggregator = eventAggregator;
			_clock = clock ?? TimeProvider.System;
		}

		#region Gate

		/// <summary>The LMS half of the gate: a current member with Manage Courses (or admin) and the add-on's authoring gate open.</summary>
		public static string LmsRefusal(LmsActor actor)
		{
			if (actor == null || !LmsCourseKnowledgeSource.GateOpen(actor) || actor.Gate?.CanAuthor != true)
				return LmsStudioOutcomes.NotEntitled;
			return actor.MayManage ? null : LmsStudioOutcomes.NotAllowed;
		}

		private async Task<LmsActor> ActorAsync(int departmentId, string userId)
		{
			try
			{
				return await _gate.GetActorAsync(departmentId, userId);
			}
			catch (Exception ex)
			{
				// Unknown access is no access.
				Logging.LogException(ex, $"The LMS gate could not be read for department {departmentId}.");
				return null;
			}
		}

		private async Task<bool> AiEntitledAsync(int departmentId)
		{
			try
			{
				return await _knowledge.IsEntitledAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The knowledge entitlement could not be read for department {departmentId}.");
				return false;
			}
		}

		public async Task<LmsStudioStatus> GetStatusAsync(int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var status = new LmsStudioStatus { AiReason = LmsStudioOutcomes.NotEntitled };
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return status;
			var actor = await ActorAsync(departmentId, userId);
			status.CanAuthor = LmsRefusal(actor) == null;
			var refused = LmsRefusal(actor);
			if (refused != null)
			{
				status.AiReason = refused;
				return status;
			}
			if (!await AiEntitledAsync(departmentId))
				return status;
			if (!AiKnowledgeService.OperatorConfigured())
			{
				status.AiReason = LmsStudioOutcomes.Unconfigured;
				return status;
			}
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, departmentId))
			{
				status.AiReason = LmsStudioOutcomes.KeptLocal;
				return status;
			}
			status.AiAvailable = true;
			status.AiReason = null;
			return status;
		}

		#endregion Gate

		#region Sources

		public async Task<List<LmsDraftableProtocol>> GetDraftableProtocolsAsync(int departmentId, string userId)
		{
			var list = new List<LmsDraftableProtocol>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return list;
			var reader = await _protocolAccess.GetReaderAsync(departmentId, userId);
			if (reader == null || !reader.MayAuthor)
				return list;

			var candidates = (await _protocols.GetProtocolSummariesForDepartmentAsync(departmentId) ?? new List<DispatchProtocol>())
				.Where(p => p != null && p.DepartmentId == departmentId && p.Status == (int)ProtocolStatuses.Published && p.CurrentVersionId.HasValue && ProtocolLicensing.CanUseForAi(p))
				.ToList();
			foreach (var protocol in await _protocolAccess.FilterReadableAsync(reader, candidates) ?? new List<DispatchProtocol>())
			{
				var version = await _library.GetVersionAsync(departmentId, protocol.CurrentVersionId.Value);
				if (version == null || version.DispatchProtocolId != protocol.DispatchProtocolId || version.Status != (int)ProtocolVersionStatuses.Published)
					continue;
				list.Add(new LmsDraftableProtocol { ProtocolId = protocol.DispatchProtocolId, Name = protocol.Name, Code = protocol.Code, VersionId = version.ProtocolVersionId, VersionLabel = version.Label });
			}
			return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
		}

		/// <summary>The protocol version the request names (or the version in force), when the member may read and author it and it may go to AI.</summary>
		private async Task<LmsStudioSource> LoadProtocolAsync(LmsCourseDraftRequest request, CancellationToken cancellationToken)
		{
			var reader = await _protocolAccess.GetReaderAsync(request.DepartmentId, request.UserId);
			if (reader == null || !reader.MayAuthor)
				return LmsStudioSource.Fail(LmsStudioOutcomes.NotAllowed);
			var protocol = request.ProtocolId > 0 ? await _protocolAccess.GetReadableAsync(request.DepartmentId, request.UserId, request.ProtocolId) : null;
			if (protocol == null || protocol.DepartmentId != request.DepartmentId || protocol.DispatchProtocolId != request.ProtocolId || protocol.IsDeleted)
				return LmsStudioSource.Fail(LmsStudioOutcomes.NotFound);
			if (!ProtocolLicensing.CanUseForAi(protocol))
				return LmsStudioSource.Fail(LmsStudioOutcomes.Excluded);

			var versionId = request.ProtocolVersionId ?? protocol.CurrentVersionId;
			var version = versionId.HasValue ? await _library.GetVersionAsync(request.DepartmentId, versionId.Value) : null;
			if (version == null || version.DepartmentId != request.DepartmentId || version.DispatchProtocolId != protocol.DispatchProtocolId ||
				(version.Status != (int)ProtocolVersionStatuses.Published && version.Status != (int)ProtocolVersionStatuses.Superseded))
				return LmsStudioSource.Fail(LmsStudioOutcomes.NotFound);

			return await LmsStudioSources.ProtocolAsync(_pages, request.DepartmentId, protocol, version, cancellationToken);
		}

		#endregion Sources

		#region Draft

		private static LmsCourseDraftResult Refused(string outcome) => new LmsCourseDraftResult { Outcome = outcome, PromptVersion = AiCourseStudioPrompt.CourseVersion };

		public async Task<LmsCourseDraftResult> DraftAsync(LmsCourseDraftRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.UserId) || !Enum.IsDefined(typeof(LmsCourseDraftSourceKinds), request.SourceKind))
				return Refused(LmsStudioOutcomes.Invalid);

			// 1. The gates, failing closed, before any content is read.
			var actor = await ActorAsync(request.DepartmentId, request.UserId);
			var lmsRefusal = LmsRefusal(actor);
			if (lmsRefusal != null)
				return Refused(lmsRefusal);
			if (!await AiEntitledAsync(request.DepartmentId))
				return Refused(LmsStudioOutcomes.NotEntitled);
			if (!AiKnowledgeService.OperatorConfigured())
				return Refused(LmsStudioOutcomes.Unconfigured);

			// 2. The source.
			LmsStudioSource source;
			try
			{
				source = request.SourceKind == (int)LmsCourseDraftSourceKinds.ProtocolVersion
					? await LoadProtocolAsync(request, cancellationToken)
					: LmsStudioSources.Document(_extractor, request.FileName, request.ContentType, request.Data, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				Logging.LogException(ex, "A course draft source could not be read.");
				return Refused(LmsStudioOutcomes.Unavailable);
			}
			if (source?.Outcome != null)
				return Refused(source.Outcome);
			if (source == null || source.Sections.Sum(s => s.Text.Length) < MinimumContentCharacters)
				return Refused(LmsStudioOutcomes.NoContent);

			// 3. Advanced Data Protection keeps department content local.
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, request.DepartmentId))
				return Refused(LmsStudioOutcomes.KeptLocal);

			// 4. The model call, audited and validated.
			var aiActor = new AdminAssistActor(request.DepartmentId, request.UserId, request.Locale ?? "en");
			var admission = await KnowledgeModelCalls.ReserveAsync(_usage, _admission, _clock, request.DepartmentId, Feature, ReservedTokens, cancellationToken);
			if (admission.Outcome != null)
				return Refused(admission.Outcome);

			var used = 0;
			var ledger = "Unavailable";
			var result = Refused(LmsStudioOutcomes.Unavailable);
			try
			{
				try
				{
					await _conversationProtection.PreflightAsync(aiActor, cancellationToken);
				}
				catch (UnauthorizedAccessException)
				{
					result.Outcome = LmsStudioOutcomes.Unconfigured;
					return result;
				}

				var numbered = AiCourseStudioPrompt.Number(source.Sections.Select(s => (source.Title, (string)source.VersionLabel, (string)s.HeadingPath, s.Text)).ToList(), out var truncated);
				result.Truncated = truncated;
				// Number() keeps the sections in order (all non-empty), so S{i+1} is the i-th section.
				var sectionByRef = numbered.Select((s, i) => (s.Ref, Section: source.Sections[i])).ToDictionary(x => x.Ref, x => x.Section, StringComparer.Ordinal);
				var questionCount = Math.Clamp(request.QuestionCount, LmsCourseDraftRequest.MinQuestions, LmsCourseDraftRequest.MaxQuestions);

				LlmResult completion;
				using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(KnowledgeConfig.AnswerTimeoutSeconds * 4, 20, 300)));
					try
					{
						completion = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, AiCourseStudioPrompt.CourseMessages(numbered, questionCount), new[] { AiCourseStudioPrompt.Tool(0) },
							Math.Clamp(KnowledgeConfig.AnswerMaxOutputTokens * 6, 2048, 8192),
							AiCacheIsolation.SaltFor(request.DepartmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
					}
					catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
					{
						return result;
					}
				}

				used = Math.Min(admission.Reservation.Tokens, Math.Max(0, completion.InputTokens + completion.OutputTokens));
				AiStudioCourse validated;
				try
				{
					using var document = AiCourseStudioPrompt.Parse(completion, 0);
					validated = AiCourseStudioPrompt.ValidateCourse(document, numbered);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
				{
					// Nothing the model wrote is kept.
					ledger = "InvalidOutput";
					result.Outcome = LmsStudioOutcomes.Refused;
					result.ExchangeId = await LogAsync(aiActor, source, LmsStudioOutcomes.Refused, null, completion, cancellationToken);
					return result;
				}

				ledger = "Answered";
				result.Dropped = validated.Dropped;

				// 5. The draft course (a draft only) and its provenance.
				var plan = BuildPlan(request, source, validated, sectionByRef);
				var written = await LmsCoursePlanWriter.WriteAsync(_courses, _banks, actor, plan, cancellationToken);
				if (!written.Succeeded)
				{
					result.Outcome = LmsStudioOutcomes.NotSaved;
					result.ExchangeId = await LogAsync(aiActor, source, LmsStudioOutcomes.NotSaved, null, completion, cancellationToken);
					return result;
				}

				result.Outcome = LmsStudioOutcomes.Created;
				result.LmsCourseId = written.Course.LmsCourseId;
				result.LmsCourseVersionId = written.Version.LmsCourseVersionId;
				result.LmsQuestionBankId = written.LmsQuestionBankId;
				result.Modules = written.Modules;
				result.Lessons = written.Lessons;
				result.Questions = written.Questions;
				result.ExchangeId = await LogAsync(aiActor, source, LmsStudioOutcomes.Drafted, written, completion, cancellationToken);
				await SaveOriginAsync(request, source, plan, written, result.ExchangeId, cancellationToken);
				Audit(request.DepartmentId, request.UserId, AuditLogTypes.LmsCourseDraftedWithAi, JsonConvert.SerializeObject(new
				{
					written.Course.LmsCourseId,
					written.Version.LmsCourseVersionId,
					Source = SourceKey(request, source),
					result.Modules,
					result.Lessons,
					result.Questions,
					result.Dropped,
					result.ExchangeId
				}));
				return result;
			}
			finally
			{
				await KnowledgeModelCalls.CompleteAsync(_usage, admission.Reservation, used, ledger);
			}
		}

		private static string SourceKey(LmsCourseDraftRequest request, LmsStudioSource source) =>
			request.SourceKind == (int)LmsCourseDraftSourceKinds.ProtocolVersion ? LmsCourseOriginKeys.Protocol(source.ItemId, source.VersionId) : LmsCourseOriginKeys.Document(source.FileName);

		/// <summary>
		/// The validated draft as a course plan: each lesson is the model's introduction and key points, then the cited sections' own text under
		/// their headings, then the citation line(s) (linked for a protocol). Questions keep their explanation with the cited section named.
		/// </summary>
		public static LmsCoursePlan BuildPlan(LmsCourseDraftRequest request, LmsStudioSource source, AiStudioCourse draft, IReadOnlyDictionary<string, LmsStudioSection> sectionByRef)
		{
			var locale = request.Locale;
			var title = !string.IsNullOrWhiteSpace(request.Title) ? request.Title.Trim() : !string.IsNullOrWhiteSpace(draft.Title) ? draft.Title : source.Title;
			title = Truncate(title, LmsCourse.TitleMaxLength);
			var sourceLabel = string.IsNullOrWhiteSpace(source.VersionLabel) ? source.Title : source.Title + " " + source.VersionLabel;

			var overview = new StringBuilder();
			if (!string.IsNullOrWhiteSpace(draft.Summary))
				overview.Append("<p>").Append(WebUtility.HtmlEncode(draft.Summary)).Append("</p>");
			overview.Append("<p><em>").Append(WebUtility.HtmlEncode(string.Format(CultureInfo.InvariantCulture, LmsText.Get("Studio.DraftOverviewNote", locale), sourceLabel))).Append("</em></p>");

			var plan = new LmsCoursePlan
			{
				Title = title,
				Summary = Truncate(draft.Summary, LmsCourse.SummaryMaxLength),
				Tags = LmsText.Get("Studio.DraftTag", locale),
				Hours = 0,
				OverviewHtml = overview.ToString(),
				BankName = Truncate(string.Format(CultureInfo.InvariantCulture, LmsText.Get("Studio.DraftBankName", locale), title), 250),
				BankDescription = LmsText.Get("Studio.DraftBankDescription", locale),
				CheckModuleTitle = LmsText.Get("Studio.CheckModuleTitle", locale),
				CheckLessonTitle = LmsText.Get("Studio.CheckLessonTitle", locale),
				CheckPassMark = 80
			};

			foreach (var module in draft.Modules)
			{
				var planned = new LmsCoursePlanModule { Title = Truncate(module.Title, 250) };
				foreach (var lesson in module.Lessons)
				{
					var cited = lesson.Refs.Where(sectionByRef.ContainsKey).Select(r => sectionByRef[r]).ToList();
					var html = new StringBuilder();
					if (!string.IsNullOrWhiteSpace(lesson.Intro))
						html.Append("<p>").Append(WebUtility.HtmlEncode(lesson.Intro)).Append("</p>");
					if (lesson.KeyPoints.Count > 0)
					{
						html.Append("<p><strong>").Append(WebUtility.HtmlEncode(LmsText.Get("Studio.KeyPoints", locale))).Append("</strong></p><ul>");
						foreach (var point in lesson.KeyPoints)
							html.Append("<li>").Append(WebUtility.HtmlEncode(point)).Append("</li>");
						html.Append("</ul>");
					}

					// The cited sections' own text, merged per anchor in source order: the source's words, numbers and doses as written.
					foreach (var group in cited.GroupBy(s => s.Anchor ?? string.Empty).ToList())
					{
						var heading = group.First().HeadingPath;
						html.Append("<h3>").Append(WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(heading) ? LmsText.Get("Studio.FromTheSource", locale) : heading)).Append("</h3>");
						html.Append("<blockquote>").Append(LmsStudioSources.Paragraphs(string.Join("\n", source.Sections.Where(s => string.Equals(s.Anchor ?? string.Empty, group.Key, StringComparison.Ordinal)).Select(s => s.Text)))).Append("</blockquote>");
					}

					var labels = cited.GroupBy(s => s.Anchor ?? string.Empty).Select(g => source.Label(g.First())).ToList();
					html.Append("<p><em>").Append(WebUtility.HtmlEncode(LmsText.Get("Studio.CitedLabel", locale))).Append(' ');
					var parts = cited.GroupBy(s => s.Anchor ?? string.Empty).Select(g =>
					{
						var label = WebUtility.HtmlEncode(source.Label(g.First()));
						// Stored lesson html keeps only absolute links (the sanitizer drops relative ones): the system's own address in front.
						var url = AbsoluteUrl(source.Url(g.Key));
						return string.IsNullOrWhiteSpace(url) ? label : "<a href=\"" + WebUtility.HtmlEncode(url) + "\">" + label + "</a>";
					});
					html.Append(string.Join("; ", parts)).Append("</em></p>");

					planned.Lessons.Add(new LmsCoursePlanLesson
					{
						Title = Truncate(lesson.Title, LmsLesson.TitleMaxLength),
						Html = html.ToString(),
						Anchors = cited.Select(s => s.Anchor).Distinct(StringComparer.Ordinal).ToList(),
						CitationLabels = labels
					});
				}
				plan.Modules.Add(planned);
			}

			foreach (var question in draft.Questions)
			{
				var cited = question.Refs.Where(sectionByRef.ContainsKey).Select(r => source.Label(sectionByRef[r])).Distinct().ToList();
				plan.Questions.Add(new LmsCoursePlanQuestion
				{
					Text = question.Text,
					Options = question.Options.ToList(),
					CorrectIndex = question.CorrectIndex,
					Explanation = question.Explanation + (cited.Count == 0 ? string.Empty : " (" + LmsText.Get("Studio.CitedLabel", locale) + " " + string.Join("; ", cited) + ")"),
					Kind = (int)LmsQuestionKinds.SingleChoice
				});
			}
			return plan;
		}

		private async Task SaveOriginAsync(LmsCourseDraftRequest request, LmsStudioSource source, LmsCoursePlan plan, LmsCoursePlanResult written, string exchangeId,
			CancellationToken cancellationToken)
		{
			var lessons = plan.Modules.SelectMany(m => m.Lessons).ToList();
			var citations = lessons.Select((l, i) => new LmsLessonCitation
			{
				LmsLessonId = i < written.LessonIds.Count ? written.LessonIds[i] : 0,
				LessonTitle = l.Title,
				Anchors = l.Anchors,
				Labels = l.CitationLabels
			}).ToList();
			var isProtocol = request.SourceKind == (int)LmsCourseDraftSourceKinds.ProtocolVersion;
			try
			{
				await _origins.InsertAsync(new LmsCourseOrigin
				{
					DepartmentId = request.DepartmentId,
					LmsCourseId = written.Course.LmsCourseId,
					LmsCourseVersionId = written.Version.LmsCourseVersionId,
					OriginKind = (int)(isProtocol ? LmsCourseOriginKinds.AiDraftFromProtocol : LmsCourseOriginKinds.AiDraftFromDocument),
					SourceKey = SourceKey(request, source),
					SourceName = Truncate(source.Title, LmsCourseOrigin.SourceNameMaxLength),
					SourceVersion = Truncate(source.VersionLabel, LmsCourseOrigin.SourceVersionMaxLength),
					SourceUrl = Truncate(isProtocol ? source.Url(null) : null, LmsCourseOrigin.SourceUrlMaxLength),
					License = "department-own",
					Attribution = Truncate(string.Format(CultureInfo.InvariantCulture, LmsText.Get("Studio.DraftAttribution", request.Locale),
						string.IsNullOrWhiteSpace(source.VersionLabel) ? source.Title : source.Title + " " + source.VersionLabel), LmsCourseOrigin.AttributionMaxLength),
					ExchangeId = exchangeId,
					CitationsJson = JsonConvert.SerializeObject(citations),
					CreatedOn = _clock.GetUtcNow().UtcDateTime,
					CreatedByUserId = request.UserId
				}, cancellationToken, true);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// The draft stands (its lessons carry their citations); only the provenance row is missing.
				Logging.LogException(ex, $"The origin of drafted course {written.Course.LmsCourseId} could not be saved.");
			}
		}

		/// <summary>One AiGenerations row per draft: the source and the course made (never the content), encrypted by the conversation protection.</summary>
		private Task<string> LogAsync(AdminAssistActor actor, LmsStudioSource source, string outcome, LmsCoursePlanResult written, LlmResult completion, CancellationToken cancellationToken)
		{
			var item = source.IsProtocol ? LmsCourseOriginKeys.Protocol(source.ItemId, source.VersionId) : "document";
			var stored = "course-draft " + item + (written?.Course == null ? string.Empty : " course:" + written.Course.LmsCourseId.ToString(CultureInfo.InvariantCulture) +
				" version:" + written.Version.LmsCourseVersionId.ToString(CultureInfo.InvariantCulture));
			var evidence = source.Sections.Select(s => item + "#" + (s.Anchor ?? string.Empty)).Distinct(StringComparer.Ordinal).Take(50);
			return KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, actor, AiCourseStudioPrompt.CourseVersion, stored, evidence, outcome,
				completion?.InputTokens ?? 0, completion?.OutputTokens ?? 0, cancellationToken);
		}

		#endregion Draft

		public async Task<LmsCourseOrigin> GetOriginAsync(int departmentId, int lmsCourseId)
		{
			if (departmentId <= 0 || lmsCourseId <= 0)
				return null;
			var origin = await _origins.GetForCourseAsync(departmentId, lmsCourseId);
			return origin != null && origin.DepartmentId == departmentId && origin.LmsCourseId == lmsCourseId ? origin : null;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}

		private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);

		/// <summary>A site path as an absolute https/http link on this system's web address; null when there is none to use.</summary>
		public static string AbsoluteUrl(string path) => ProtocolAiSources.AbsoluteUrl(path);
	}
}
