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
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>
	/// Protocol flashcards (round 7A; §16.4 "flashcards flagged for review when their source version changes", PowerDMS PowerRecall precedent),
	/// Enhanced AI only. Drafting needs K1's knowledge entitlement (fails closed) and the protocol author or approver right; the source is the
	/// protocol's published version in force (never "no AI" or licensed content; Advanced Data Protection keeps it local); every card cites one
	/// section and holds no number that section lacks. Drafts are kept only in the encrypted AiGenerations row; an author accepts cards by index
	/// and they are read back from that row (never from the client) and checked against the version in force again. Members who may read the
	/// protocol see its active cards drafted from the version in force. Practice only: this service holds no writer of learning records,
	/// certifications, enrollments or attempts, so practice is never graded or recorded (guard tested); spacing stays in the member's browser.
	/// </summary>
	public class ProtocolFlashcardService : IProtocolFlashcardService
	{
		public const string Feature = "ProtocolFlashcards";
		public const int ReservedTokens = 16000;
		public const int MinimumContentCharacters = 40;
		public const int MaxCardsPerProtocol = 300;

		private readonly IAiKnowledgeService _knowledge;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolLibraryService _library;
		private readonly IKnowledgePageTextSource _pages;
		private readonly IProtocolFlashcardsRepository _cards;
		private readonly IAdminAssistConversationProtection _conversationProtection;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly IKnowledgeGapsRepository _generations;
		private readonly IEventAggregator _eventAggregator;
		private readonly TimeProvider _clock;

		public ProtocolFlashcardService(IAiKnowledgeService knowledge, IDepartmentDataProtectionService protection, IProtocolAccessService access, IProtocolLibraryService library,
			IKnowledgePageTextSource pages, IProtocolFlashcardsRepository cards, IAdminAssistConversationProtection conversationProtection, IAiBackgroundAdmission admission,
			IAiUsageMeter usage, Lazy<ILlmClient> client, IKnowledgeGapsRepository generations, IEventAggregator eventAggregator, TimeProvider clock)
		{
			_knowledge = knowledge;
			_protection = protection;
			_access = access;
			_library = library;
			_pages = pages;
			_cards = cards;
			_conversationProtection = conversationProtection;
			_admission = admission;
			_usage = usage;
			_client = client;
			_generations = generations;
			_eventAggregator = eventAggregator;
			_clock = clock ?? TimeProvider.System;
		}

		#region Gate and source

		public async Task<ProtocolAiStatus> GetStatusAsync(int departmentId, string userId, int protocolId, CancellationToken cancellationToken = default)
		{
			if (!await ProtocolAiGate.EntitledAsync(_knowledge, departmentId))
				return new ProtocolAiStatus { Reason = ProtocolAiOutcomes.NotEntitled };
			var (protocol, _, refusal) = await ProtocolAiGate.AuthorableAsync(_access, departmentId, userId, protocolId);
			if (refusal != null)
				return new ProtocolAiStatus { Reason = refusal };
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

		/// <summary>The protocol's version in force as a source (readable, allowed to AI), or the refusal.</summary>
		private async Task<(ProtocolAiSource Source, string Refusal)> SourceAsync(int departmentId, DispatchProtocol protocol, CancellationToken cancellationToken)
		{
			if (!ProtocolLicensing.CanUseForAi(protocol))
				return (null, ProtocolAiOutcomes.Excluded);
			var version = await ProtocolAiGate.VersionInForceAsync(_library, departmentId, protocol);
			if (version == null)
				return (null, ProtocolAiOutcomes.NotFound);
			return (await ProtocolAiSources.FromVersionAsync(_pages, departmentId, protocol, version, cancellationToken), null);
		}

		#endregion Gate and source

		#region Draft and accept

		/// <summary>What is kept (encrypted, in AiGenerations) to accept from: the protocol, the version and each card with its section's hash.</summary>
		private sealed class StoredCards
		{
			public int P { get; set; }
			public int V { get; set; }
			public string L { get; set; }
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

		private static ProtocolFlashcardDraft Refused(string outcome) => new ProtocolFlashcardDraft { Outcome = outcome, PromptVersion = AiProtocolStudioPrompt.CardsVersion };

		public async Task<ProtocolFlashcardDraft> DraftAsync(ProtocolFlashcardDraftRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.UserId) || request.ProtocolId <= 0)
				return Refused(ProtocolAiOutcomes.Invalid);
			if (!await ProtocolAiGate.EntitledAsync(_knowledge, request.DepartmentId))
				return Refused(ProtocolAiOutcomes.NotEntitled);
			var (protocol, _, refusal) = await ProtocolAiGate.AuthorableAsync(_access, request.DepartmentId, request.UserId, request.ProtocolId);
			if (refusal != null)
				return Refused(refusal);
			if (!AiKnowledgeService.OperatorConfigured())
				return Refused(ProtocolAiOutcomes.Unconfigured);

			var (source, sourceRefusal) = await SourceAsync(request.DepartmentId, protocol, cancellationToken);
			if (sourceRefusal != null)
				return Refused(sourceRefusal);
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

				var numbered = AiCourseStudioPrompt.Number(source.Sections.Select(s => (source.Title, (string)source.VersionLabel, (string)s.HeadingPath, s.Text)).ToList(), out _);
				var count = Math.Clamp(request.Count, ProtocolFlashcardDraftRequest.MinCards, ProtocolFlashcardDraftRequest.MaxCards);
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
					// Nothing the model wrote is shown.
					ledger = "InvalidOutput";
					draft.Outcome = ProtocolAiOutcomes.Refused;
					draft.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, aiActor, AiProtocolStudioPrompt.CardsVersion,
						"protocol-flashcards protocol:" + protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture), Evidence(source), ProtocolAiOutcomes.Refused,
						completion.InputTokens, completion.OutputTokens, cancellationToken);
					return draft;
				}

				ledger = "Answered";
				var sectionByRef = numbered.Select((s, i) => (s.Ref, Section: source.Sections[i])).ToDictionary(x => x.Ref, x => x.Section, StringComparer.Ordinal);
				var stored = new StoredCards
				{
					P = protocol.DispatchProtocolId,
					V = source.VersionId,
					L = source.VersionLabel,
					Cards = validated.Cards.Select(c => new StoredCard
					{
						F = c.Front,
						B = c.Back,
						A = sectionByRef[c.Ref].Anchor,
						H = sectionByRef[c.Ref].HeadingPath,
						X = KnowledgeChunker.Hash(source.TextOf(sectionByRef[c.Ref].Anchor))
					}).ToList()
				};
				draft.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, aiActor, AiProtocolStudioPrompt.CardsVersion,
					System.Text.Json.JsonSerializer.Serialize(stored), Evidence(source), ProtocolAiOutcomes.Drafted, completion.InputTokens, completion.OutputTokens, cancellationToken);
				if (draft.ExchangeId == null)
				{
					// A draft that cannot be read back cannot be accepted: it is not offered.
					draft.Outcome = ProtocolAiOutcomes.Unavailable;
					return draft;
				}

				draft.Outcome = ProtocolAiOutcomes.Drafted;
				draft.Dropped = validated.Dropped;
				draft.Cards = stored.Cards.Select(c => new ProtocolFlashcardDraftCard { Front = c.F, Back = c.B, Anchor = c.A, Heading = c.H }).ToList();
				return draft;
			}
			finally
			{
				await KnowledgeModelCalls.CompleteAsync(_usage, admission.Reservation, used, ledger);
			}
		}

		private static IEnumerable<string> Evidence(ProtocolAiSource source)
		{
			var item = "protocol:" + source.ProtocolId.ToString(CultureInfo.InvariantCulture) + ":" + source.VersionId.ToString(CultureInfo.InvariantCulture);
			return source.Sections.Select(s => item + "#" + (s.Anchor ?? string.Empty)).Distinct(StringComparer.Ordinal).Take(50);
		}

		public async Task<ProtocolFlashcardResult> AcceptAsync(int departmentId, string userId, int protocolId, string exchangeId, IReadOnlyCollection<int> cardIndexes,
			CancellationToken cancellationToken = default)
		{
			if (!Guid.TryParseExact(exchangeId ?? string.Empty, "D", out _) || cardIndexes == null || cardIndexes.Count == 0)
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.Invalid);
			if (!await ProtocolAiGate.EntitledAsync(_knowledge, departmentId))
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.NotEntitled);
			var (protocol, _, refusal) = await ProtocolAiGate.AuthorableAsync(_access, departmentId, userId, protocolId);
			if (refusal != null)
				return ProtocolFlashcardResult.Fail(refusal);

			// The author's own draft, as stored: never what the client sends back.
			var row = await _generations.GetGenerationAsync(departmentId, userId, exchangeId, cancellationToken);
			if (row == null || !string.Equals(row.PromptVersion, AiProtocolStudioPrompt.CardsVersion, StringComparison.Ordinal) ||
				!string.Equals(row.Outcome, ProtocolAiOutcomes.Drafted, StringComparison.Ordinal))
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.NotFound);

			StoredCards stored;
			try
			{
				var content = await _conversationProtection.ReadAsync(new AdminAssistActor(departmentId, userId, "en"), row, cancellationToken);
				stored = string.IsNullOrWhiteSpace(content?.Question) ? null : System.Text.Json.JsonSerializer.Deserialize<StoredCards>(content.Question);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, "A protocol flashcard draft could not be read back.");
				stored = null;
			}
			if (stored == null || stored.P != protocol.DispatchProtocolId || stored.Cards == null || stored.Cards.Count == 0)
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.NotFound);

			// The version must still be the one in force, and each chosen card's section unchanged and still holding its numbers.
			var (source, sourceRefusal) = await SourceAsync(departmentId, protocol, cancellationToken);
			if (sourceRefusal != null)
				return ProtocolFlashcardResult.Fail(sourceRefusal);
			if (source.VersionId != stored.V)
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.Stale);

			var existing = await _cards.GetForProtocolAsync(departmentId, protocol.DispatchProtocolId) ?? new List<ProtocolFlashcard>();
			var fronts = new HashSet<string>(existing.Select(c => c.Front ?? string.Empty), StringComparer.OrdinalIgnoreCase);
			var order = existing.Select(c => c.SortOrder).DefaultIfEmpty(0).Max();
			var now = _clock.GetUtcNow().UtcDateTime;
			var saved = 0;
			foreach (var index in cardIndexes.Distinct().Where(i => i >= 0 && i < stored.Cards.Count).OrderBy(i => i))
			{
				var card = stored.Cards[index];
				var text = source.TextOf(card.A);
				if (existing.Count + saved >= MaxCardsPerProtocol || string.IsNullOrWhiteSpace(card.F) || string.IsNullOrWhiteSpace(card.B) || fronts.Contains(card.F) ||
					!source.HasAnchor(card.A) || !string.Equals(KnowledgeChunker.Hash(text), card.X, StringComparison.Ordinal) || !AiCourseStudioPrompt.Quoted(card.F + "\n" + card.B, new[] { text }))
					continue;

				await _cards.InsertAsync(new ProtocolFlashcard
				{
					DepartmentId = departmentId,
					DispatchProtocolId = protocol.DispatchProtocolId,
					ProtocolVersionId = stored.V,
					SourceVersionLabel = Truncate(stored.L, ProtocolFlashcard.VersionLabelMaxLength),
					SourceAnchor = Truncate(card.A, ProtocolFlashcard.AnchorMaxLength),
					SourceHeading = Truncate(card.H, ProtocolFlashcard.HeadingMaxLength),
					Front = Truncate(card.F, ProtocolFlashcard.FrontMaxLength),
					Back = Truncate(card.B, ProtocolFlashcard.BackMaxLength),
					Status = (int)ProtocolFlashcardStatuses.Active,
					SortOrder = ++order,
					ExchangeId = exchangeId,
					CreatedOn = now,
					CreatedByUserId = userId
				}, cancellationToken, true);
				fronts.Add(card.F);
				saved++;
			}

			if (saved > 0)
				Audit(departmentId, userId, new { protocol.DispatchProtocolId, accepted = saved, exchangeId });
			return new ProtocolFlashcardResult { Outcome = ProtocolAiOutcomes.Created, Count = saved };
		}

		#endregion Draft and accept

		#region Author management

		public async Task<List<ProtocolFlashcard>> GetCardsAsync(int departmentId, string userId, int protocolId)
		{
			var (protocol, _, refusal) = await ProtocolAiGate.AuthorableAsync(_access, departmentId, userId, protocolId);
			if (refusal != null)
				return new List<ProtocolFlashcard>();

			var cards = await _cards.GetForProtocolAsync(departmentId, protocol.DispatchProtocolId) ?? new List<ProtocolFlashcard>();
			// Repair pass: an active card whose version is no longer the one in force (the hook missed it), or whose protocol is now kept from AI,
			// is flagged now; the author sees it as such.
			var inForce = ProtocolLicensing.CanUseForAi(protocol) ? (await ProtocolAiGate.VersionInForceAsync(_library, departmentId, protocol))?.ProtocolVersionId ?? 0 : 0;
			var reason = ProtocolLicensing.CanUseForAi(protocol) && inForce > 0 ? ProtocolFlashcardFlagReasons.SourceSuperseded : ProtocolFlashcardFlagReasons.SourceUnavailable;
			var stale = cards.Where(c => c.IsActive && c.ProtocolVersionId != inForce).Select(c => c.ProtocolFlashcardId).ToList();
			if (stale.Count > 0)
			{
				var now = _clock.GetUtcNow().UtcDateTime;
				await _cards.FlagCardsAsync(departmentId, stale, (int)reason, now);
				foreach (var card in cards.Where(c => stale.Contains(c.ProtocolFlashcardId)))
				{
					card.Status = (int)ProtocolFlashcardStatuses.NeedsReview;
					card.FlagReason = (int)reason;
					card.FlaggedOn = now;
				}
			}
			return cards;
		}

		/// <summary>A card the actor may manage, with its protocol.</summary>
		private async Task<(ProtocolFlashcard Card, DispatchProtocol Protocol, string Refusal)> ManagedCardAsync(int departmentId, string userId, int protocolFlashcardId)
		{
			var card = protocolFlashcardId > 0 ? await _cards.GetByIdForDepartmentAsync(departmentId, protocolFlashcardId) : null;
			if (card == null || card.DepartmentId != departmentId)
				return (null, null, ProtocolAiOutcomes.NotFound);
			var (protocol, _, refusal) = await ProtocolAiGate.AuthorableAsync(_access, departmentId, userId, card.DispatchProtocolId);
			return refusal != null ? (null, null, refusal) : (card, protocol, null);
		}

		public async Task<ProtocolFlashcardResult> SaveAsync(int departmentId, string userId, int protocolFlashcardId, string front, string back, CancellationToken cancellationToken = default)
		{
			var (card, protocol, refusal) = await ManagedCardAsync(departmentId, userId, protocolFlashcardId);
			if (refusal != null)
				return ProtocolFlashcardResult.Fail(refusal);
			var f = Clean(front);
			var b = Clean(back);
			if (f == null || b == null || f.Length > ProtocolFlashcard.FrontMaxLength || b.Length > ProtocolFlashcard.BackMaxLength)
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.Invalid);

			// A card stays true to its cited section: every number on it must be written in that section of its version.
			var version = await _library.GetVersionAsync(departmentId, card.ProtocolVersionId);
			if (version == null || version.DispatchProtocolId != protocol.DispatchProtocolId || version.DepartmentId != departmentId ||
				(version.Status != (int)ProtocolVersionStatuses.Published && version.Status != (int)ProtocolVersionStatuses.Superseded))
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.NotFound);
			var source = await ProtocolAiSources.FromVersionAsync(_pages, departmentId, protocol, version, cancellationToken);
			if (!source.HasAnchor(card.SourceAnchor))
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.NotFound);
			if (!AiCourseStudioPrompt.Quoted(f + "\n" + b, new[] { source.TextOf(card.SourceAnchor) }))
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.Refused);

			card.Front = f;
			card.Back = b;
			await _cards.UpdateAsync(card, cancellationToken, true);
			Audit(departmentId, userId, new { card.DispatchProtocolId, card.ProtocolFlashcardId, edited = true });
			return new ProtocolFlashcardResult { Outcome = ProtocolAiOutcomes.Created, Count = 1, Card = card };
		}

		public async Task<ProtocolFlashcardResult> ReviewAsync(int departmentId, string userId, int protocolFlashcardId, CancellationToken cancellationToken = default)
		{
			var (card, protocol, refusal) = await ManagedCardAsync(departmentId, userId, protocolFlashcardId);
			if (refusal != null)
				return ProtocolFlashcardResult.Fail(refusal);

			// Against the version in force: its section must still be there and still hold every number the card shows.
			var (source, sourceRefusal) = await SourceAsync(departmentId, protocol, cancellationToken);
			if (sourceRefusal != null)
				return ProtocolFlashcardResult.Fail(sourceRefusal);
			if (!source.HasAnchor(card.SourceAnchor))
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.NotFound);
			if (!AiCourseStudioPrompt.Quoted(card.Front + "\n" + card.Back, new[] { source.TextOf(card.SourceAnchor) }))
				return ProtocolFlashcardResult.Fail(ProtocolAiOutcomes.Refused);

			card.ProtocolVersionId = source.VersionId;
			card.SourceVersionLabel = Truncate(source.VersionLabel, ProtocolFlashcard.VersionLabelMaxLength);
			card.SourceHeading = Truncate(source.Sections.FirstOrDefault(s => string.Equals(s.Anchor ?? string.Empty, card.SourceAnchor ?? string.Empty, StringComparison.Ordinal))?.HeadingPath
				?? card.SourceHeading, ProtocolFlashcard.HeadingMaxLength);
			card.Status = (int)ProtocolFlashcardStatuses.Active;
			card.FlagReason = (int)ProtocolFlashcardFlagReasons.None;
			card.FlaggedOn = null;
			card.ReviewedOn = _clock.GetUtcNow().UtcDateTime;
			card.ReviewedByUserId = userId;
			await _cards.UpdateAsync(card, cancellationToken, true);
			Audit(departmentId, userId, new { card.DispatchProtocolId, card.ProtocolFlashcardId, reviewedAgainst = source.VersionId });
			return new ProtocolFlashcardResult { Outcome = ProtocolAiOutcomes.Created, Count = 1, Card = card };
		}

		public async Task<ProtocolFlashcardResult> RemoveAsync(int departmentId, string userId, int protocolFlashcardId, CancellationToken cancellationToken = default)
		{
			var (card, _, refusal) = await ManagedCardAsync(departmentId, userId, protocolFlashcardId);
			if (refusal != null)
				return ProtocolFlashcardResult.Fail(refusal);
			await _cards.DeleteAsync(card, cancellationToken);
			Audit(departmentId, userId, new { card.DispatchProtocolId, card.ProtocolFlashcardId, removed = true });
			return new ProtocolFlashcardResult { Outcome = ProtocolAiOutcomes.Created, Count = 1 };
		}

		#endregion Author management

		#region Member deck

		public async Task<List<ProtocolFlashcardDeckCard>> GetDeckAsync(int departmentId, string userId, int protocolId, CancellationToken cancellationToken = default)
		{
			var deck = new List<ProtocolFlashcardDeckCard>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || protocolId <= 0)
				return deck;
			// Enhanced AI only (no LMS), failing closed.
			if (!await ProtocolAiGate.EntitledAsync(_knowledge, departmentId))
				return deck;

			// The library's own read rule; a protocol now kept from AI shows no cards.
			var protocol = await _access.GetReadableAsync(departmentId, userId, protocolId);
			if (protocol == null || protocol.DepartmentId != departmentId || protocol.DispatchProtocolId != protocolId || protocol.IsDeleted || !ProtocolLicensing.CanUseForAi(protocol))
				return deck;
			var inForce = await ProtocolAiGate.VersionInForceAsync(_library, departmentId, protocol);
			if (inForce == null)
				return deck;

			// Read only: a card drafted from another version is simply not shown (the author's page flags it).
			foreach (var card in (await _cards.GetForProtocolAsync(departmentId, protocolId) ?? new List<ProtocolFlashcard>())
				.Where(c => c.IsActive && c.DepartmentId == departmentId && c.ProtocolVersionId == inForce.ProtocolVersionId))
			{
				deck.Add(new ProtocolFlashcardDeckCard
				{
					Id = card.ProtocolFlashcardId,
					Front = card.Front,
					Back = card.Back,
					SourceVersionLabel = card.SourceVersionLabel,
					SourceHeading = card.SourceHeading,
					SourceUrl = KnowledgeLinks.WebPath((int)KnowledgeSourceKinds.Protocol, protocolId.ToString(CultureInfo.InvariantCulture),
						card.ProtocolVersionId.ToString(CultureInfo.InvariantCulture), card.SourceAnchor)
				});
			}
			return deck;
		}

		#endregion Member deck

		private void Audit(int departmentId, string userId, object after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.ProtocolFlashcardsChanged,
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
	/// The protocol flashcard supersede hook (round 7A): a protocol version going into force flags every active card of that protocol drafted
	/// from another version for review. One update by department and protocol; never fails the publish.
	/// </summary>
	public sealed class ProtocolFlashcardSupersedeHook : IContentVersionPublishedHook
	{
		private readonly IProtocolFlashcardsRepository _cards;

		public ProtocolFlashcardSupersedeHook(IProtocolFlashcardsRepository cards)
		{
			_cards = cards;
		}

		public async Task OnVersionPublishedAsync(ContentVersionPublished published, CancellationToken cancellationToken = default)
		{
			if (published == null || published.Kind != ContentVersionKinds.ProtocolVersion || published.DepartmentId <= 0 || published.ItemId <= 0 || published.VersionId <= 0)
				return;
			var flagged = await _cards.FlagOtherVersionsAsync(published.DepartmentId, published.ItemId, published.VersionId, (int)ProtocolFlashcardFlagReasons.SourceSuperseded,
				published.PublishedOnUtc == default ? DateTime.UtcNow : published.PublishedOnUtc, cancellationToken);
			if (flagged > 0)
				Logging.LogInfo($"{flagged} protocol flashcard(s) of department {published.DepartmentId} flagged for review: protocol {published.ItemId} is now at version {published.VersionId}.");
		}
	}
}
