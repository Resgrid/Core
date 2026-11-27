using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>
	/// Course flashcards (T5 item 5, §16.4 "flashcards flagged for review when their source version changes", PowerDMS PowerRecall precedent).
	/// Drafting needs both gates (the LMS add-on and Enhanced AI's knowledge entitlement, failing closed) and the course's edit right; the source is
	/// the course's version in force or a protocol the course was drafted from or embeds (readable, allowed to AI); every card cites one section
	/// and holds no number that section lacks. Drafts are kept only in the encrypted AiGenerations row; an author accepts cards by index and
	/// the cards are read back from that row (never from the client) and checked against the source again. Learners see a course's active cards
	/// whose source version is still in force. Practice only: this service holds no writer of learning records, certifications, enrollments,
	/// progress or attempts, so practice is never graded or recorded (guard tested); the learner's spacing state stays in their browser.
	/// </summary>
	public class LmsFlashcardService : ILmsFlashcardService
	{
		public const string Feature = "Flashcards";
		public const int ReservedTokens = 16000;
		public const int MinimumContentCharacters = 40;
		public const int MaxCardsPerCourse = 300;

		private readonly IAiKnowledgeService _knowledge;
		private readonly ILmsGateService _gate;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IProtocolAccessService _protocolAccess;
		private readonly IProtocolLibraryService _library;
		private readonly IKnowledgePageTextSource _pages;
		private readonly ILmsCourseService _courses;
		private readonly ILmsCourseOriginsRepository _origins;
		private readonly ILmsFlashcardsRepository _cards;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly IAdminAssistConversationProtection _conversationProtection;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly IKnowledgeGapsRepository _generations;
		private readonly IEventAggregator _eventAggregator;
		private readonly TimeProvider _clock;

		public LmsFlashcardService(IAiKnowledgeService knowledge, ILmsGateService gate, IDepartmentDataProtectionService protection, IProtocolAccessService protocolAccess,
			IProtocolLibraryService library, IKnowledgePageTextSource pages, ILmsCourseService courses, ILmsCourseOriginsRepository origins, ILmsFlashcardsRepository cards,
			ILmsEnrollmentsRepository enrollments, IAdminAssistConversationProtection conversationProtection, IAiBackgroundAdmission admission, IAiUsageMeter usage,
			Lazy<ILlmClient> client, IKnowledgeGapsRepository generations, IEventAggregator eventAggregator, TimeProvider clock)
		{
			_knowledge = knowledge;
			_gate = gate;
			_protection = protection;
			_protocolAccess = protocolAccess;
			_library = library;
			_pages = pages;
			_courses = courses;
			_origins = origins;
			_cards = cards;
			_enrollments = enrollments;
			_conversationProtection = conversationProtection;
			_admission = admission;
			_usage = usage;
			_client = client;
			_generations = generations;
			_eventAggregator = eventAggregator;
			_clock = clock ?? TimeProvider.System;
		}

		#region Gate and course

		private async Task<LmsActor> ActorAsync(int departmentId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return null;
			try
			{
				return await _gate.GetActorAsync(departmentId, userId);
			}
			catch (Exception ex)
			{
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

		/// <summary>The department course the actor may edit with the authoring gate open, or the refusal.</summary>
		private async Task<(LmsCourse Course, string Refusal)> EditableCourseAsync(LmsActor actor, int lmsCourseId)
		{
			if (actor == null || !LmsCourseKnowledgeSource.GateOpen(actor) || actor.Gate?.CanAuthor != true)
				return (null, LmsStudioOutcomes.NotEntitled);
			var course = lmsCourseId > 0 ? await _courses.GetCourseAsync(actor.DepartmentId, lmsCourseId) : null;
			if (course == null || course.IsSystem || course.IsDeleted || course.DepartmentId != actor.DepartmentId)
				return (null, LmsStudioOutcomes.NotFound);
			var rights = await _courses.GetRightsAsync(actor, course);
			return rights?.Edit == true ? (course, null) : (null, LmsStudioOutcomes.NotAllowed);
		}

		#endregion Gate and course

		#region Sources

		/// <summary>The course's version in force as a source, or null when it has none.</summary>
		private async Task<LmsStudioSource> CourseSourceAsync(LmsCourse course)
		{
			if (!course.CurrentVersionId.HasValue)
				return null;
			var outline = await _courses.GetOutlineAsync(course.DepartmentId, course.CurrentVersionId.Value);
			if (outline?.Version == null || outline.Version.LmsCourseId != course.LmsCourseId || outline.Version.Status != (int)ProtocolVersionStatuses.Published)
				return null;
			return LmsStudioSources.Course(outline);
		}

		/// <summary>A protocol version as a source, when the member may read the protocol and it may go to AI.</summary>
		private async Task<LmsStudioSource> ProtocolSourceAsync(int departmentId, string userId, int protocolId, int? versionId, CancellationToken cancellationToken)
		{
			var protocol = protocolId > 0 ? await _protocolAccess.GetReadableAsync(departmentId, userId, protocolId) : null;
			if (protocol == null || protocol.DepartmentId != departmentId || protocol.DispatchProtocolId != protocolId || protocol.IsDeleted)
				return LmsStudioSource.Fail(LmsStudioOutcomes.NotFound);
			if (!ProtocolLicensing.CanUseForAi(protocol))
				return LmsStudioSource.Fail(LmsStudioOutcomes.Excluded);
			var id = versionId ?? protocol.CurrentVersionId;
			var version = id.HasValue ? await _library.GetVersionAsync(departmentId, id.Value) : null;
			if (version == null || version.DispatchProtocolId != protocolId || version.DepartmentId != departmentId ||
				(version.Status != (int)ProtocolVersionStatuses.Published && version.Status != (int)ProtocolVersionStatuses.Superseded))
				return LmsStudioSource.Fail(LmsStudioOutcomes.NotFound);
			return await LmsStudioSources.ProtocolAsync(_pages, departmentId, protocol, version, cancellationToken);
		}

		/// <summary>The protocols a course is built from: the one it was drafted from, and those its version in force embeds.</summary>
		private async Task<List<int>> CourseProtocolIdsAsync(LmsCourse course)
		{
			var ids = new List<int>();
			var origin = await _origins.GetForCourseAsync(course.DepartmentId, course.LmsCourseId);
			if (origin != null && LmsCourseOriginKeys.TryParseProtocol(origin.SourceKey, out var originProtocol, out _))
				ids.Add(originProtocol);
			if (course.CurrentVersionId.HasValue)
			{
				var outline = await _courses.GetOutlineAsync(course.DepartmentId, course.CurrentVersionId.Value);
				foreach (var lesson in (outline?.Lessons ?? Enumerable.Empty<LmsLesson>()).Where(l => l.Kind == (int)LmsLessonKinds.ProtocolVersion && l.ProtocolVersionId.HasValue))
				{
					var version = await _library.GetVersionAsync(course.DepartmentId, lesson.ProtocolVersionId.Value);
					if (version != null)
						ids.Add(version.DispatchProtocolId);
				}
			}
			return ids.Distinct().ToList();
		}

		public async Task<List<LmsFlashcardSourceChoice>> GetSourceChoicesAsync(int departmentId, string userId, int lmsCourseId)
		{
			var choices = new List<LmsFlashcardSourceChoice>();
			var actor = await ActorAsync(departmentId, userId);
			var (course, refusal) = await EditableCourseAsync(actor, lmsCourseId);
			if (refusal != null)
				return choices;

			var own = await CourseSourceAsync(course);
			if (own != null)
				choices.Add(new LmsFlashcardSourceChoice
				{
					SourceKind = (int)LmsFlashcardSourceKinds.CourseVersion, ItemId = course.LmsCourseId, VersionId = own.VersionId, Title = own.Title, VersionLabel = own.VersionLabel
				});

			foreach (var protocolId in await CourseProtocolIdsAsync(course))
			{
				var protocol = await _protocolAccess.GetReadableAsync(departmentId, userId, protocolId);
				if (protocol == null || protocol.DepartmentId != departmentId || !ProtocolLicensing.CanUseForAi(protocol) || !protocol.CurrentVersionId.HasValue ||
					protocol.Status != (int)ProtocolStatuses.Published)
					continue;
				var version = await _library.GetVersionAsync(departmentId, protocol.CurrentVersionId.Value);
				if (version == null || version.Status != (int)ProtocolVersionStatuses.Published)
					continue;
				choices.Add(new LmsFlashcardSourceChoice
				{
					SourceKind = (int)LmsFlashcardSourceKinds.ProtocolVersion, ItemId = protocol.DispatchProtocolId, VersionId = version.ProtocolVersionId, Title = protocol.Name,
					VersionLabel = version.Label
				});
			}
			return choices;
		}

		#endregion Sources

		#region Draft and accept

		/// <summary>What is kept (encrypted, in AiGenerations) to accept from: the course, the source version and each card with its section's hash.</summary>
		private sealed class StoredCards
		{
			public int C { get; set; }
			public int K { get; set; }
			public int I { get; set; }
			public int V { get; set; }
			public string L { get; set; }
			public string T { get; set; }
			public List<StoredCard> Cards { get; set; } = new List<StoredCard>();
		}

		private sealed class StoredCard
		{
			public string F { get; set; }
			public string B { get; set; }
			public string A { get; set; }
			public string H { get; set; }
			public string X { get; set; }
		}

		private static LmsFlashcardDraft Refused(string outcome) => new LmsFlashcardDraft { Outcome = outcome, PromptVersion = AiCourseStudioPrompt.CardsVersion };

		public async Task<LmsFlashcardDraft> DraftAsync(LmsFlashcardDraftRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.UserId) || request.LmsCourseId <= 0 ||
				!Enum.IsDefined(typeof(LmsFlashcardSourceKinds), request.SourceKind))
				return Refused(LmsStudioOutcomes.Invalid);

			var actor = await ActorAsync(request.DepartmentId, request.UserId);
			var (course, refusal) = await EditableCourseAsync(actor, request.LmsCourseId);
			if (refusal != null)
				return Refused(refusal);
			if (!await AiEntitledAsync(request.DepartmentId))
				return Refused(LmsStudioOutcomes.NotEntitled);
			if (!AiKnowledgeService.OperatorConfigured())
				return Refused(LmsStudioOutcomes.Unconfigured);

			LmsStudioSource source;
			if (request.SourceKind == (int)LmsFlashcardSourceKinds.CourseVersion)
			{
				source = await CourseSourceAsync(course) ?? LmsStudioSource.Fail(LmsStudioOutcomes.NotFound);
			}
			else
			{
				// Only a protocol the course is built from: cards belong to the course's own material.
				if (!(await CourseProtocolIdsAsync(course)).Contains(request.ProtocolId))
					return Refused(LmsStudioOutcomes.NotFound);
				source = await ProtocolSourceAsync(request.DepartmentId, request.UserId, request.ProtocolId, null, cancellationToken);
			}
			if (source.Outcome != null)
				return Refused(source.Outcome);
			if (source.Sections.Sum(s => s.Text.Length) < MinimumContentCharacters)
				return Refused(LmsStudioOutcomes.NoContent);
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, request.DepartmentId))
				return Refused(LmsStudioOutcomes.KeptLocal);

			var aiActor = new AdminAssistActor(request.DepartmentId, request.UserId, request.Locale ?? "en");
			var admission = await KnowledgeModelCalls.ReserveAsync(_usage, _admission, _clock, request.DepartmentId, Feature, ReservedTokens, cancellationToken);
			if (admission.Outcome != null)
				return Refused(admission.Outcome);

			var used = 0;
			var ledger = "Unavailable";
			var draft = Refused(LmsStudioOutcomes.Unavailable);
			draft.SourceTitle = source.Title;
			draft.SourceVersionLabel = source.VersionLabel;
			try
			{
				try
				{
					await _conversationProtection.PreflightAsync(aiActor, cancellationToken);
				}
				catch (UnauthorizedAccessException)
				{
					draft.Outcome = LmsStudioOutcomes.Unconfigured;
					return draft;
				}

				var numbered = AiCourseStudioPrompt.Number(source.Sections.Select(s => (source.Title, (string)source.VersionLabel, (string)s.HeadingPath, s.Text)).ToList(), out _);
				var count = Math.Clamp(request.Count, LmsFlashcardDraftRequest.MinCards, LmsFlashcardDraftRequest.MaxCards);
				LlmResult completion;
				using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(KnowledgeConfig.AnswerTimeoutSeconds * 2, 10, 180)));
					try
					{
						completion = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, AiCourseStudioPrompt.CardsMessages(numbered, count), new[] { AiCourseStudioPrompt.Tool(1) },
							Math.Clamp(KnowledgeConfig.AnswerMaxOutputTokens * 3, 1024, 4096),
							AiCacheIsolation.SaltFor(request.DepartmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
					}
					catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
					{
						return draft;
					}
				}

				used = Math.Min(admission.Reservation.Tokens, Math.Max(0, completion.InputTokens + completion.OutputTokens));
				AiStudioCards validated;
				try
				{
					using var document = AiCourseStudioPrompt.Parse(completion, 1);
					validated = AiCourseStudioPrompt.ValidateCards(document, numbered);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
				{
					ledger = "InvalidOutput";
					draft.Outcome = LmsStudioOutcomes.Refused;
					draft.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, aiActor, AiCourseStudioPrompt.CardsVersion,
						"flashcards course:" + course.LmsCourseId.ToString(CultureInfo.InvariantCulture), Evidence(source), LmsStudioOutcomes.Refused,
						completion.InputTokens, completion.OutputTokens, cancellationToken);
					return draft;
				}

				ledger = "Answered";
				var sectionByRef = numbered.Select((s, i) => (s.Ref, Section: source.Sections[i])).ToDictionary(x => x.Ref, x => x.Section, StringComparer.Ordinal);
				var stored = new StoredCards
				{
					C = course.LmsCourseId,
					K = request.SourceKind,
					I = source.ItemId,
					V = source.VersionId,
					L = source.VersionLabel,
					T = source.Title,
					Cards = validated.Cards.Select(c => new StoredCard
					{
						F = c.Front,
						B = c.Back,
						A = sectionByRef[c.Ref].Anchor,
						H = sectionByRef[c.Ref].HeadingPath,
						X = KnowledgeChunker.Hash(source.TextOf(sectionByRef[c.Ref].Anchor))
					}).ToList()
				};
				draft.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, aiActor, AiCourseStudioPrompt.CardsVersion,
					System.Text.Json.JsonSerializer.Serialize(stored), Evidence(source), LmsStudioOutcomes.Drafted, completion.InputTokens, completion.OutputTokens, cancellationToken);
				if (draft.ExchangeId == null)
				{
					// A draft that cannot be read back cannot be accepted: it is not offered.
					draft.Outcome = LmsStudioOutcomes.Unavailable;
					return draft;
				}

				draft.Outcome = LmsStudioOutcomes.Drafted;
				draft.Dropped = validated.Dropped;
				draft.Cards = stored.Cards.Select(c => new LmsFlashcardDraftCard { Front = c.F, Back = c.B, Anchor = c.A, Heading = c.H }).ToList();
				return draft;
			}
			finally
			{
				await KnowledgeModelCalls.CompleteAsync(_usage, admission.Reservation, used, ledger);
			}
		}

		private static IEnumerable<string> Evidence(LmsStudioSource source)
		{
			var item = (source.IsProtocol ? "protocol:" : "course:") + source.ItemId.ToString(CultureInfo.InvariantCulture) + ":" + source.VersionId.ToString(CultureInfo.InvariantCulture);
			return source.Sections.Select(s => item + "#" + (s.Anchor ?? string.Empty)).Distinct(StringComparer.Ordinal).Take(50);
		}

		public async Task<LmsFlashcardResult> AcceptAsync(int departmentId, string userId, int lmsCourseId, string exchangeId, IReadOnlyCollection<int> cardIndexes,
			CancellationToken cancellationToken = default)
		{
			if (!Guid.TryParseExact(exchangeId ?? string.Empty, "D", out _) || cardIndexes == null || cardIndexes.Count == 0)
				return LmsFlashcardResult.Fail(LmsStudioOutcomes.Invalid);
			var actor = await ActorAsync(departmentId, userId);
			var (course, refusal) = await EditableCourseAsync(actor, lmsCourseId);
			if (refusal != null)
				return LmsFlashcardResult.Fail(refusal);
			if (!await AiEntitledAsync(departmentId))
				return LmsFlashcardResult.Fail(LmsStudioOutcomes.NotEntitled);

			// The author's own draft, as stored: never what the client sends back.
			var row = await _generations.GetGenerationAsync(departmentId, userId, exchangeId, cancellationToken);
			if (row == null || !string.Equals(row.PromptVersion, AiCourseStudioPrompt.CardsVersion, StringComparison.Ordinal) ||
				!string.Equals(row.Outcome, LmsStudioOutcomes.Drafted, StringComparison.Ordinal))
				return LmsFlashcardResult.Fail(LmsStudioOutcomes.NotFound);

			StoredCards stored;
			try
			{
				var content = await _conversationProtection.ReadAsync(new AdminAssistActor(departmentId, userId, "en"), row, cancellationToken);
				stored = string.IsNullOrWhiteSpace(content?.Question) ? null : System.Text.Json.JsonSerializer.Deserialize<StoredCards>(content.Question);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, "A flashcard draft could not be read back.");
				stored = null;
			}
			if (stored == null || stored.C != course.LmsCourseId || stored.Cards == null || stored.Cards.Count == 0)
				return LmsFlashcardResult.Fail(LmsStudioOutcomes.NotFound);

			// The source must still be the same version, and each chosen card's section unchanged and still holding its numbers.
			var source = stored.K == (int)LmsFlashcardSourceKinds.CourseVersion
				? await CourseSourceAsync(course)
				: await ProtocolSourceAsync(departmentId, userId, stored.I, null, cancellationToken);
			if (source == null || source.Outcome != null || source.VersionId != stored.V)
				return LmsFlashcardResult.Fail(LmsStudioOutcomes.Refused);

			var existing = await _cards.GetForCourseAsync(departmentId, course.LmsCourseId) ?? new List<LmsFlashcard>();
			var fronts = new HashSet<string>(existing.Select(c => c.Front ?? string.Empty), StringComparer.OrdinalIgnoreCase);
			var order = existing.Select(c => c.SortOrder).DefaultIfEmpty(0).Max();
			var now = _clock.GetUtcNow().UtcDateTime;
			var saved = 0;
			foreach (var index in cardIndexes.Distinct().Where(i => i >= 0 && i < stored.Cards.Count).OrderBy(i => i))
			{
				var card = stored.Cards[index];
				var text = source.TextOf(card.A);
				if (existing.Count + saved >= MaxCardsPerCourse || string.IsNullOrWhiteSpace(card.F) || string.IsNullOrWhiteSpace(card.B) || fronts.Contains(card.F) ||
					!source.HasAnchor(card.A) || !string.Equals(KnowledgeChunker.Hash(text), card.X, StringComparison.Ordinal) || !AiCourseStudioPrompt.Quoted(card.F + "\n" + card.B, new[] { text }))
					continue;

				await _cards.InsertAsync(new LmsFlashcard
				{
					DepartmentId = departmentId,
					LmsCourseId = course.LmsCourseId,
					SourceKind = stored.K,
					SourceItemId = stored.I,
					SourceVersionId = stored.V,
					SourceVersionLabel = Truncate(stored.L, 50),
					SourceAnchor = Truncate(card.A, LmsFlashcard.AnchorMaxLength),
					SourceHeading = Truncate(card.H, LmsFlashcard.HeadingMaxLength),
					SourceTitle = Truncate(stored.T, LmsFlashcard.TitleMaxLength),
					Front = Truncate(card.F, LmsFlashcard.FrontMaxLength),
					Back = Truncate(card.B, LmsFlashcard.BackMaxLength),
					Status = (int)LmsFlashcardStatuses.Active,
					SortOrder = ++order,
					ExchangeId = exchangeId,
					CreatedOn = now,
					CreatedByUserId = userId
				}, cancellationToken, true);
				fronts.Add(card.F);
				saved++;
			}

			if (saved > 0)
				Audit(departmentId, userId, new { course.LmsCourseId, accepted = saved, exchangeId });
			return new LmsFlashcardResult { Outcome = LmsStudioOutcomes.Created, Count = saved };
		}

		#endregion Draft and accept

		#region Author management

		public async Task<List<LmsFlashcard>> GetCardsAsync(int departmentId, string userId, int lmsCourseId)
		{
			var actor = await ActorAsync(departmentId, userId);
			var (course, refusal) = await EditableCourseAsync(actor, lmsCourseId);
			if (refusal != null)
				return new List<LmsFlashcard>();

			var cards = await _cards.GetForCourseAsync(departmentId, course.LmsCourseId) ?? new List<LmsFlashcard>();
			// Repair pass: an active card whose source moved on without the hook seeing it is flagged now (the author sees it as such).
			var stale = new List<int>();
			foreach (var card in cards.Where(c => c.IsActive))
			{
				var inForce = await VersionInForceAsync(departmentId, course, card.SourceKind, card.SourceItemId);
				if (inForce != card.SourceVersionId)
					stale.Add(card.LmsFlashcardId);
			}
			if (stale.Count > 0)
			{
				var now = _clock.GetUtcNow().UtcDateTime;
				await _cards.FlagCardsAsync(departmentId, stale, (int)LmsFlashcardFlagReasons.SourceSuperseded, now);
				foreach (var card in cards.Where(c => stale.Contains(c.LmsFlashcardId)))
				{
					card.Status = (int)LmsFlashcardStatuses.NeedsReview;
					card.FlagReason = (int)LmsFlashcardFlagReasons.SourceSuperseded;
					card.FlaggedOn = now;
				}
			}
			return cards;
		}

		/// <summary>The version of a card's source in force now (0 when there is none).</summary>
		private async Task<int> VersionInForceAsync(int departmentId, LmsCourse course, int sourceKind, int sourceItemId)
		{
			if (sourceKind == (int)LmsFlashcardSourceKinds.CourseVersion)
			{
				if (sourceItemId == course.LmsCourseId)
					return course.Status == (int)ProtocolStatuses.Published ? course.CurrentVersionId ?? 0 : 0;
				var other = await _courses.GetCourseAsync(departmentId, sourceItemId);
				return other != null && other.DepartmentId == departmentId && !other.IsDeleted && other.Status == (int)ProtocolStatuses.Published ? other.CurrentVersionId ?? 0 : 0;
			}

			var versions = await _library.GetVersionsAsync(departmentId, sourceItemId) ?? new List<ProtocolVersion>();
			return versions.FirstOrDefault(v => v.DepartmentId == departmentId && v.DispatchProtocolId == sourceItemId && v.Status == (int)ProtocolVersionStatuses.Published)
				?.ProtocolVersionId ?? 0;
		}

		/// <summary>A card the actor may manage, with its course.</summary>
		private async Task<(LmsFlashcard Card, LmsCourse Course, string Refusal)> ManagedCardAsync(int departmentId, string userId, int lmsFlashcardId)
		{
			var card = await _cards.GetByIdForDepartmentAsync(departmentId, lmsFlashcardId);
			if (card == null || card.DepartmentId != departmentId)
				return (null, null, LmsStudioOutcomes.NotFound);
			var actor = await ActorAsync(departmentId, userId);
			var (course, refusal) = await EditableCourseAsync(actor, card.LmsCourseId);
			return refusal != null ? (null, null, refusal) : (card, course, null);
		}

		/// <summary>The card's source at a version (its recorded one, or the one in force), or a refusal.</summary>
		private async Task<LmsStudioSource> CardSourceAsync(int departmentId, string userId, LmsCourse course, LmsFlashcard card, int? versionId, CancellationToken cancellationToken)
		{
			if (card.SourceKind == (int)LmsFlashcardSourceKinds.CourseVersion)
			{
				var id = versionId ?? course.CurrentVersionId;
				if (!id.HasValue || card.SourceItemId != course.LmsCourseId)
					return LmsStudioSource.Fail(LmsStudioOutcomes.NotFound);
				var outline = await _courses.GetOutlineAsync(departmentId, id.Value);
				if (outline?.Version == null || outline.Version.LmsCourseId != course.LmsCourseId ||
					(outline.Version.Status != (int)ProtocolVersionStatuses.Published && outline.Version.Status != (int)ProtocolVersionStatuses.Superseded))
					return LmsStudioSource.Fail(LmsStudioOutcomes.NotFound);
				return LmsStudioSources.Course(outline);
			}
			return await ProtocolSourceAsync(departmentId, userId, card.SourceItemId, versionId, cancellationToken);
		}

		public async Task<LmsFlashcardResult> SaveAsync(int departmentId, string userId, int lmsFlashcardId, string front, string back, CancellationToken cancellationToken = default)
		{
			var (card, course, refusal) = await ManagedCardAsync(departmentId, userId, lmsFlashcardId);
			if (refusal != null)
				return LmsFlashcardResult.Fail(refusal);
			var f = Clean(front);
			var b = Clean(back);
			if (f == null || b == null || f.Length > LmsFlashcard.FrontMaxLength || b.Length > LmsFlashcard.BackMaxLength)
				return LmsFlashcardResult.Fail(LmsStudioOutcomes.Invalid);

			// A card stays true to its cited section: every number on it must be written in that section.
			var source = await CardSourceAsync(departmentId, userId, course, card, card.SourceVersionId, cancellationToken);
			if (source.Outcome != null || !source.HasAnchor(card.SourceAnchor))
				return LmsFlashcardResult.Fail(source.Outcome ?? LmsStudioOutcomes.NotFound);
			if (!AiCourseStudioPrompt.Quoted(f + "\n" + b, new[] { source.TextOf(card.SourceAnchor) }))
				return LmsFlashcardResult.Fail(LmsStudioOutcomes.Refused);

			card.Front = f;
			card.Back = b;
			await _cards.UpdateAsync(card, cancellationToken, true);
			Audit(departmentId, userId, new { card.LmsCourseId, card.LmsFlashcardId, edited = true });
			return new LmsFlashcardResult { Outcome = LmsStudioOutcomes.Created, Count = 1, Card = card };
		}

		public async Task<LmsFlashcardResult> ReviewAsync(int departmentId, string userId, int lmsFlashcardId, CancellationToken cancellationToken = default)
		{
			var (card, course, refusal) = await ManagedCardAsync(departmentId, userId, lmsFlashcardId);
			if (refusal != null)
				return LmsFlashcardResult.Fail(refusal);

			// Against the version in force: its section must still be there and still hold every number the card shows.
			var source = await CardSourceAsync(departmentId, userId, course, card, null, cancellationToken);
			if (source.Outcome != null || source.VersionId <= 0)
				return LmsFlashcardResult.Fail(source.Outcome ?? LmsStudioOutcomes.NotFound);
			var anchor = source.HasAnchor(card.SourceAnchor) ? card.SourceAnchor : await CourseAnchorInAsync(departmentId, course, card, source);
			if (anchor == null || !source.HasAnchor(anchor))
				return LmsFlashcardResult.Fail(LmsStudioOutcomes.NotFound);
			if (!AiCourseStudioPrompt.Quoted(card.Front + "\n" + card.Back, new[] { source.TextOf(anchor) }))
				return LmsFlashcardResult.Fail(LmsStudioOutcomes.Refused);

			card.SourceAnchor = Truncate(anchor, LmsFlashcard.AnchorMaxLength);
			card.SourceVersionId = source.VersionId;
			card.SourceVersionLabel = Truncate(source.VersionLabel, 50);
			card.SourceTitle = Truncate(source.Title, LmsFlashcard.TitleMaxLength);
			card.Status = (int)LmsFlashcardStatuses.Active;
			card.FlagReason = (int)LmsFlashcardFlagReasons.None;
			card.FlaggedOn = null;
			card.ReviewedOn = _clock.GetUtcNow().UtcDateTime;
			card.ReviewedByUserId = userId;
			await _cards.UpdateAsync(card, cancellationToken, true);
			Audit(departmentId, userId, new { card.LmsCourseId, card.LmsFlashcardId, reviewedAgainst = source.VersionId });
			return new LmsFlashcardResult { Outcome = LmsStudioOutcomes.Created, Count = 1, Card = card };
		}

		/// <summary>
		/// A course card's anchor in another version of the course: a new version copies its lessons under new ids, so the card's lesson is found
		/// again by its module and lesson title (the block or heading anchor inside the lesson is kept). Null when the lesson is gone.
		/// </summary>
		private async Task<string> CourseAnchorInAsync(int departmentId, LmsCourse course, LmsFlashcard card, LmsStudioSource target)
		{
			if (card.SourceKind != (int)LmsFlashcardSourceKinds.CourseVersion || !KnowledgeLinks.TryParseCourseAnchor(card.SourceAnchor, out var lessonId, out var inner))
				return null;
			var before = await _courses.GetOutlineAsync(departmentId, card.SourceVersionId);
			var after = await _courses.GetOutlineAsync(departmentId, target.VersionId);
			if (before?.Version == null || after?.Version == null || before.Version.LmsCourseId != course.LmsCourseId || after.Version.LmsCourseId != course.LmsCourseId)
				return null;

			string Key(LmsOutlineModule module, LmsLesson lesson) => (module.Module?.Title ?? string.Empty).Trim() + "\n" + (lesson.Title ?? string.Empty).Trim();
			var old = before.Modules.SelectMany(m => m.Lessons.Select(l => (Key: Key(m, l), Lesson: l))).FirstOrDefault(x => x.Lesson.LmsLessonId == lessonId);
			if (old.Lesson == null)
				return null;
			var matches = after.Modules.SelectMany(m => m.Lessons.Select(l => (Key: Key(m, l), Lesson: l))).Where(x => string.Equals(x.Key, old.Key, StringComparison.Ordinal)).ToList();
			return matches.Count == 1 ? KnowledgeLinks.CourseAnchor(matches[0].Lesson.LmsLessonId, inner) : null;
		}

		public async Task<LmsFlashcardResult> RemoveAsync(int departmentId, string userId, int lmsFlashcardId, CancellationToken cancellationToken = default)
		{
			var (card, _, refusal) = await ManagedCardAsync(departmentId, userId, lmsFlashcardId);
			if (refusal != null)
				return LmsFlashcardResult.Fail(refusal);
			await _cards.DeleteAsync(card, cancellationToken);
			Audit(departmentId, userId, new { card.LmsCourseId, card.LmsFlashcardId, removed = true });
			return new LmsFlashcardResult { Outcome = LmsStudioOutcomes.Created, Count = 1 };
		}

		#endregion Author management

		#region Learner deck

		public async Task<List<LmsFlashcardDeckCard>> GetDeckAsync(int departmentId, string userId, int lmsEnrollmentId, CancellationToken cancellationToken = default)
		{
			var deck = new List<LmsFlashcardDeckCard>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || lmsEnrollmentId <= 0)
				return deck;

			var enrollment = await _enrollments.GetByIdForDepartmentAsync(departmentId, lmsEnrollmentId);
			if (enrollment == null || enrollment.DepartmentId != departmentId || !string.Equals(enrollment.UserId, userId, StringComparison.OrdinalIgnoreCase) ||
				enrollment.Status == (int)LmsEnrollmentStatuses.Withdrawn)
				return deck;

			// Both gates (§17.2: flashcards need the LMS and Enhanced AI), failing closed.
			var actor = await ActorAsync(departmentId, userId);
			if (!LmsCourseKnowledgeSource.GateOpen(actor) || !await AiEntitledAsync(departmentId))
				return deck;

			var course = await _courses.GetCourseAsync(departmentId, enrollment.LmsCourseId);
			if (course == null || course.IsSystem || course.IsDeleted || course.DepartmentId != departmentId)
				return deck;
			// Round 10 A7: a department course's practice deck is a learner item that needs the member's LMS seat.
			if (LmsLearnerSeats.Needed(course, userId) && !await _gate.HoldsLearnerSeatAsync(departmentId, userId))
				return deck;

			var cards = (await _cards.GetForCourseAsync(departmentId, course.LmsCourseId) ?? new List<LmsFlashcard>()).Where(c => c.IsActive).ToList();
			var inForce = new Dictionary<(int, int), int>();
			var readable = new Dictionary<int, bool>();
			foreach (var card in cards)
			{
				// Read only: a card whose source version is no longer in force is simply not shown (the author's page flags it).
				if (!inForce.TryGetValue((card.SourceKind, card.SourceItemId), out var version))
					inForce[(card.SourceKind, card.SourceItemId)] = version = await VersionInForceAsync(departmentId, course, card.SourceKind, card.SourceItemId);
				if (version <= 0 || version != card.SourceVersionId)
					continue;
				if (card.SourceKind == (int)LmsFlashcardSourceKinds.ProtocolVersion)
				{
					// A learner never sees a card from a protocol they may not open (the library's own rule), nor one now kept from AI.
					if (!readable.TryGetValue(card.SourceItemId, out var allowed))
					{
						var protocol = await _protocolAccess.GetReadableAsync(departmentId, userId, card.SourceItemId);
						readable[card.SourceItemId] = allowed = protocol != null && protocol.DepartmentId == departmentId && ProtocolLicensing.CanUseForAi(protocol);
					}
					if (!allowed)
						continue;
				}

				deck.Add(new LmsFlashcardDeckCard
				{
					Id = card.LmsFlashcardId,
					Front = card.Front,
					Back = card.Back,
					SourceTitle = card.SourceTitle,
					SourceVersionLabel = card.SourceVersionLabel,
					SourceHeading = card.SourceHeading,
					SourceUrl = new LmsStudioSource
					{
						ItemId = card.SourceItemId,
						VersionId = card.SourceVersionId,
						IsProtocol = card.SourceKind == (int)LmsFlashcardSourceKinds.ProtocolVersion,
						IsCourse = card.SourceKind == (int)LmsFlashcardSourceKinds.CourseVersion
					}.Url(card.SourceAnchor)
				});
			}
			return deck;
		}

		#endregion Learner deck

		private void Audit(int departmentId, string userId, object after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.LmsFlashcardsChanged,
				After = Newtonsoft.Json.JsonConvert.SerializeObject(after),
				Successful = true,
				ServerName = Environment.MachineName
			});
		}

		private static string Clean(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var line = System.Text.RegularExpressions.Regex.Replace(new string(value.Where(ch => !char.IsControl(ch) || ch == '\n').ToArray()), @"\s+", " ").Trim();
			return line.Length == 0 ? null : line;
		}

		private static string Truncate(string value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max);
	}

	/// <summary>
	/// The supersede hook (T5 item 5): a protocol or course version going into force flags every active card drafted from an earlier version of
	/// that source for review. One update by department and source item; never fails the publish.
	/// </summary>
	public sealed class LmsFlashcardSupersedeHook : IContentVersionPublishedHook
	{
		private readonly ILmsFlashcardsRepository _cards;

		public LmsFlashcardSupersedeHook(ILmsFlashcardsRepository cards)
		{
			_cards = cards;
		}

		public static int SourceKindOf(ContentVersionKinds kind) =>
			kind == ContentVersionKinds.CourseVersion ? (int)LmsFlashcardSourceKinds.CourseVersion : (int)LmsFlashcardSourceKinds.ProtocolVersion;

		public async Task OnVersionPublishedAsync(ContentVersionPublished published, CancellationToken cancellationToken = default)
		{
			if (published == null || published.DepartmentId <= 0 || published.ItemId <= 0 || published.VersionId <= 0)
				return;
			var flagged = await _cards.FlagOtherVersionsAsync(published.DepartmentId, SourceKindOf(published.Kind), published.ItemId, published.VersionId,
				(int)LmsFlashcardFlagReasons.SourceSuperseded, published.PublishedOnUtc == default ? DateTime.UtcNow : published.PublishedOnUtc, cancellationToken);
			if (flagged > 0)
				Logging.LogInfo($"{flagged} flashcard(s) of department {published.DepartmentId} flagged for review: {published.Kind} {published.ItemId} is now at version {published.VersionId}.");
		}
	}
}
