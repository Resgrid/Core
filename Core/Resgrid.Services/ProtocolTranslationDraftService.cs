using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Ai;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>
	/// "Draft translation with AI" (round 7A), Enhanced AI only, on a draft translation of a locked protocol version (P3's translations). In order:
	/// 1. the gate: K1's knowledge entitlement (fails closed), the protocol author right (as for saving a translation), a draft translation of a
	///    locked block or html version, the protocol readable and allowed to AI ("no AI" and licensed content never sent), the operator's model;
	///    Advanced Data Protection keeps content local (no model call);
	/// 2. the translatable wording still in the source's words (what an author already translated is never replaced) is sent as numbered
	///    segments, within one draft's budget; doses, concentrations, routes and intervals, links, attachments, table shape and the structure are
	///    never sent and always stay the source's (P3's alignment);
	/// 3. each drafted segment is kept only when every number of its source appears in it unchanged, none is added, and its markup and links are
	///    the source's; a refused segment keeps its wording;
	/// 4. the result is the translation's document aligned to its source again. Nothing is saved: the author reviews the wording in the editor
	///    and saves the draft, which still needs its own L2 approval. The exchange is logged to AiGenerations.
	/// </summary>
	public class ProtocolTranslationDraftService : IProtocolTranslationDraftService
	{
		public const string Feature = "ProtocolTranslationDraft";
		public const int ReservedTokens = 24000;
		public const int MaxSingleSegmentCharacters = 20000;

		private readonly IAiKnowledgeService _knowledge;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolLibraryService _library;
		private readonly IProtocolTranslationService _translations;
		private readonly IAdminAssistConversationProtection _conversationProtection;
		private readonly IAiBackgroundAdmission _admission;
		private readonly IAiUsageMeter _usage;
		private readonly Lazy<ILlmClient> _client;
		private readonly IKnowledgeGapsRepository _generations;
		private readonly TimeProvider _clock;

		public ProtocolTranslationDraftService(IAiKnowledgeService knowledge, IDepartmentDataProtectionService protection, IProtocolAccessService access, IProtocolLibraryService library,
			IProtocolTranslationService translations, IAdminAssistConversationProtection conversationProtection, IAiBackgroundAdmission admission, IAiUsageMeter usage,
			Lazy<ILlmClient> client, IKnowledgeGapsRepository generations, TimeProvider clock)
		{
			_knowledge = knowledge;
			_protection = protection;
			_access = access;
			_library = library;
			_translations = translations;
			_conversationProtection = conversationProtection;
			_admission = admission;
			_usage = usage;
			_client = client;
			_generations = generations;
			_clock = clock ?? TimeProvider.System;
		}

		#region Gate

		private sealed record Loaded(ProtocolVersionTranslation Translation, ProtocolVersion Version, DispatchProtocol Protocol, string Refusal);

		private async Task<Loaded> LoadAsync(int departmentId, string userId, int protocolVersionTranslationId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || protocolVersionTranslationId <= 0)
				return new Loaded(null, null, null, ProtocolAiOutcomes.Invalid);
			if (!await ProtocolAiGate.EntitledAsync(_knowledge, departmentId))
				return new Loaded(null, null, null, ProtocolAiOutcomes.NotEntitled);
			var reader = await _access.GetReaderAsync(departmentId, userId);
			if (reader == null || !reader.MayAuthor)
				return new Loaded(null, null, null, ProtocolAiOutcomes.NotAllowed);

			var translation = await _translations.GetAsync(departmentId, protocolVersionTranslationId);
			if (translation == null || translation.DepartmentId != departmentId)
				return new Loaded(null, null, null, ProtocolAiOutcomes.NotFound);
			var version = await _library.GetVersionAsync(departmentId, translation.ProtocolVersionId);
			if (version == null || version.DepartmentId != departmentId || version.ProtocolVersionId != translation.ProtocolVersionId)
				return new Loaded(null, null, null, ProtocolAiOutcomes.NotFound);
			var protocol = await _access.GetReadableAsync(departmentId, userId, version.DispatchProtocolId);
			if (protocol == null || protocol.DepartmentId != departmentId || protocol.IsDeleted)
				return new Loaded(null, null, null, ProtocolAiOutcomes.NotFound);
			if (!ProtocolLicensing.CanUseForAi(protocol))
				return new Loaded(translation, version, protocol, ProtocolAiOutcomes.Excluded);
			// Only a draft translation of a locked version (P3: the version's content can no longer change under it).
			if (translation.Status != (int)ProtocolTranslationStatuses.Draft || ProtocolLifecycle.IsContentEditable(version.Status) || version.IsDocument ||
				version.Status == (int)ProtocolVersionStatuses.Legacy)
				return new Loaded(translation, version, protocol, ProtocolAiOutcomes.NotDraft);
			return new Loaded(translation, version, protocol, null);
		}

		public async Task<ProtocolAiStatus> GetStatusAsync(int departmentId, string userId, int protocolVersionTranslationId, CancellationToken cancellationToken = default)
		{
			var loaded = await LoadAsync(departmentId, userId, protocolVersionTranslationId);
			if (loaded.Refusal != null)
				return new ProtocolAiStatus { Reason = loaded.Refusal };
			if (!AiKnowledgeService.OperatorConfigured())
				return new ProtocolAiStatus { Reason = ProtocolAiOutcomes.Unconfigured };
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, departmentId))
				return new ProtocolAiStatus { Reason = ProtocolAiOutcomes.KeptLocal };
			return new ProtocolAiStatus { Available = true };
		}

		#endregion Gate

		#region Segments

		/// <summary>One translatable field: its source wording, where the drafted wording goes, and how long it may be.</summary>
		public sealed class Slot
		{
			public string Source { get; set; }
			public bool IsHtml { get; set; }
			public int MaxLength { get; set; }
			public Action<string> Apply { get; set; }
		}

		/// <summary>
		/// The fields of <paramref name="target"/> (the translation, aligned to its source) still in the source's wording, in document order; each
		/// slot writes into <paramref name="target"/>. Doses, concentration, route, interval, drug, links, attachments and table shape are not
		/// slots: they are never translated.
		/// </summary>
		public static List<Slot> Untranslated(ProtocolContentDocument source, ProtocolContentDocument target)
		{
			var slots = new List<Slot>();
			if (source == null || target == null)
				return slots;

			void Add(string sourceValue, string current, bool html, int max, Action<string> apply)
			{
				if (string.IsNullOrWhiteSpace(sourceValue) || !string.Equals((current ?? string.Empty).Trim(), sourceValue.Trim(), StringComparison.Ordinal))
					return;
				slots.Add(new Slot { Source = sourceValue, IsHtml = html, MaxLength = max, Apply = apply });
			}

			if (!source.IsBlockModel)
			{
				var html = source.Render();
				Add(html, target.Render(), true, MaxSingleSegmentCharacters, value =>
				{
					var replaced = ProtocolContentDocument.FromHtml(value);
					target.Schema = replaced.Schema;
					target.Blocks = replaced.Blocks;
					target.Anchors = replaced.Anchors;
				});
				return slots;
			}

			var blocks = new Dictionary<string, ProtocolContentBlock>(StringComparer.Ordinal);
			var items = new Dictionary<string, ProtocolStepItem>(StringComparer.Ordinal);
			var variants = new Dictionary<string, ProtocolVariant>(StringComparer.Ordinal);
			foreach (var (block, _) in ProtocolBlocks.Flatten(target))
			{
				if (block.Key != null) blocks[block.Key] = block;
				foreach (var item in block.Items ?? new List<ProtocolStepItem>()) if (item?.Key != null) items[item.Key] = item;
				foreach (var variant in block.Variants ?? new List<ProtocolVariant>()) if (variant?.Key != null) variants[variant.Key] = variant;
			}

			foreach (var (s, _) in ProtocolBlocks.Flatten(source))
			{
				if (s.Key == null || !blocks.TryGetValue(s.Key, out var t) || !string.Equals(s.Type, t.Type, StringComparison.Ordinal))
					continue;
				var richMax = s.Type == ProtocolBlockTypes.Paragraph || s.Type == ProtocolBlockTypes.Html ? ProtocolBlocks.RichMaxLength : 4000;
				Add(s.Text, t.Text, false, 200, v => t.Text = v);
				Add(s.Title, t.Title, false, 200, v => t.Title = v);
				Add(s.Html, t.Html, true, richMax, v => t.Html = v);
				Add(s.Contact, t.Contact, false, 200, v => t.Contact = v);
				Add(s.Alt, t.Alt, false, 300, v => t.Alt = v);
				Add(s.Label, t.Label, false, 200, v => t.Label = v);
				if (s.Medication != null && t.Medication != null)
				{
					var medication = t.Medication;
					Add(s.Medication.Indication, medication.Indication, false, ProtocolMedication.FieldMaxLength, v => medication.Indication = v);
					Add(s.Medication.Notes, medication.Notes, true, 4000, v => medication.Notes = v);
				}
				foreach (var item in s.Items ?? new List<ProtocolStepItem>())
					if (item?.Key != null && items.TryGetValue(item.Key, out var ti))
						Add(item.Html, ti.Html, true, 4000, v => ti.Html = v);
				if (s.Rows != null && t.Rows != null)
					for (var r = 0; r < s.Rows.Count && r < t.Rows.Count; r++)
						for (var c = 0; c < (s.Rows[r]?.Count ?? 0) && c < (t.Rows[r]?.Count ?? 0); c++)
						{
							var row = t.Rows[r];
							var column = c;
							Add(s.Rows[r][c], row[c], false, ProtocolBlocks.TextMaxLength, v => row[column] = v);
						}
				foreach (var variant in s.Variants ?? new List<ProtocolVariant>())
					if (variant?.Key != null && variants.TryGetValue(variant.Key, out var tv))
						Add(variant.Label, tv.Label, false, 100, v => tv.Label = v);
			}
			return slots;
		}

		/// <summary>The slots one draft sends: in order, within the character budget (a first slot alone may be longer, up to its own limit).</summary>
		public static List<(Slot Slot, AiTranslationSegment Segment)> Batch(IReadOnlyList<Slot> slots)
		{
			var batch = new List<(Slot, AiTranslationSegment)>();
			var total = 0;
			foreach (var slot in slots ?? Array.Empty<Slot>())
			{
				var length = slot.Source.Length;
				if (batch.Count >= AiProtocolStudioPrompt.MaxSegments || length > MaxSingleSegmentCharacters)
					break;
				if (batch.Count > 0 && total + length > AiProtocolStudioPrompt.MaxTotalSegmentCharacters)
					break;
				total += length;
				batch.Add((slot, new AiTranslationSegment("T" + (batch.Count + 1).ToString(CultureInfo.InvariantCulture), slot.Source, slot.IsHtml, slot.MaxLength)));
			}
			return batch;
		}

		#endregion Segments

		#region Draft

		private static ProtocolTranslationDraft Refused(string outcome) => new ProtocolTranslationDraft { Outcome = outcome, PromptVersion = AiProtocolStudioPrompt.TranslationVersion };

		public async Task<ProtocolTranslationDraft> DraftAsync(int departmentId, string userId, int protocolVersionTranslationId, CancellationToken cancellationToken = default)
		{
			var loaded = await LoadAsync(departmentId, userId, protocolVersionTranslationId);
			if (loaded.Refusal != null)
				return Refused(loaded.Refusal);
			if (!AiKnowledgeService.OperatorConfigured())
				return Refused(ProtocolAiOutcomes.Unconfigured);

			var source = ProtocolContentDocument.Parse(loaded.Version.ContentJson, loaded.Version.ContentHtml);
			var target = ProtocolTranslationRules.Align(source, ProtocolContentDocument.Parse(loaded.Translation.ContentJson, loaded.Translation.ContentHtml));
			var slots = Untranslated(source, target);
			var batch = Batch(slots);
			if (batch.Count == 0)
				return Refused(ProtocolAiOutcomes.NoContent);
			if (await KnowledgeModelCalls.IsProtectedAsync(_protection, departmentId))
				return Refused(ProtocolAiOutcomes.KeptLocal);

			var locale = loaded.Translation.Locale;
			var aiActor = new AdminAssistActor(departmentId, userId, locale ?? "en");
			var admission = await KnowledgeModelCalls.ReserveAsync(_usage, _admission, _clock, departmentId, Feature, ReservedTokens, cancellationToken);
			if (admission.Outcome != null)
				return Refused(admission.Outcome);

			var used = 0;
			var ledger = "Unavailable";
			var draft = Refused(ProtocolAiOutcomes.Unavailable);
			var evidence = new[] { "protocol:" + loaded.Version.DispatchProtocolId.ToString(CultureInfo.InvariantCulture) + ":" + loaded.Version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture) +
				"/translation:" + loaded.Translation.ProtocolVersionTranslationId.ToString(CultureInfo.InvariantCulture) };
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

				var segments = batch.Select(b => b.Segment).ToList();
				LlmResult completion;
				using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(KnowledgeConfig.AnswerTimeoutSeconds * 4, 20, 300)));
					try
					{
						completion = await _client.Value.CompleteAsync(new LlmRequest(AiConfig.Model, AiProtocolStudioPrompt.TranslationMessages(locale, segments),
							new[] { AiProtocolStudioPrompt.TranslationToolDefinition() }, Math.Clamp(KnowledgeConfig.AnswerMaxOutputTokens * 4, 2048, 8192),
							AiCacheIsolation.SaltFor(departmentId, AiConfig.SendCacheSalt, AiConfig.CacheSaltKey, AiConfig.AuditHmacKey)), deadline.Token);
					}
					catch (Exception ex) when (ex is LlmUnavailableException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
					{
						return draft;
					}
				}

				used = Math.Min(admission.Reservation.Tokens, Math.Max(0, completion.InputTokens + completion.OutputTokens));
				AiTranslatedSegments validated;
				try
				{
					using var document = AiProtocolStudioPrompt.Parse(completion, AiProtocolStudioPrompt.TranslationTool);
					validated = AiProtocolStudioPrompt.ValidateTranslation(document, segments);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is System.Text.Json.JsonException)
				{
					// Nothing the model wrote is shown.
					ledger = "InvalidOutput";
					draft.Outcome = ProtocolAiOutcomes.Refused;
					draft.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, aiActor, AiProtocolStudioPrompt.TranslationVersion,
						"protocol-translation translation:" + protocolVersionTranslationId.ToString(CultureInfo.InvariantCulture), evidence, ProtocolAiOutcomes.Refused,
						completion.InputTokens, completion.OutputTokens, cancellationToken);
					return draft;
				}

				ledger = "Answered";
				foreach (var (slot, segment) in batch)
					if (validated.Kept.TryGetValue(segment.Id, out var translated))
						slot.Apply(translated);

				// Aligned to the source once more: the structure, doses, links and attachments are the source's whatever was drafted.
				var result = ProtocolTranslationRules.Align(source, target);
				draft.ExchangeId = await KnowledgeModelCalls.LogAsync(_conversationProtection, _generations, _clock, aiActor, AiProtocolStudioPrompt.TranslationVersion,
					System.Text.Json.JsonSerializer.Serialize(new { t = protocolVersionTranslationId, v = loaded.Version.ProtocolVersionId, l = locale, k = validated.Kept }), evidence,
					ProtocolAiOutcomes.Drafted, completion.InputTokens, completion.OutputTokens, cancellationToken);

				draft.Outcome = ProtocolAiOutcomes.Drafted;
				draft.Document = result;
				draft.Translated = validated.Kept.Count;
				draft.Dropped = validated.Dropped;
				draft.Remaining = slots.Count - batch.Count;
				return draft;
			}
			finally
			{
				await KnowledgeModelCalls.CompleteAsync(_usage, admission.Reservation, used, ledger);
			}
		}

		#endregion Draft
	}
}
