using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// What a knowledge chunk was cut from (K1, protocols-training-knowledge-plan §16.1). Persisted in KnowledgeChunks.SourceKind and
	/// KnowledgeIndexRequests.SourceKind: append only, never renumber.
	/// </summary>
	public enum KnowledgeSourceKinds
	{
		/// <summary>A published protocol version in force: one chunk per heading section of its ContentJson html blocks.</summary>
		Protocol = 0,

		/// <summary>Text extracted from a protocol's attachments, one chunk per page (the extraction store round-3 P2 builds).</summary>
		ProtocolFile = 1,

		/// <summary>A started, unarchived training: its name, description and text by heading section. Never its quiz.</summary>
		Training = 2,

		/// <summary>Text extracted from a training's attachments, one chunk per page.</summary>
		TrainingFile = 3,

		/// <summary>A published LMS course lesson (T2 registers the source; until then nothing is indexed).</summary>
		Course = 4,

		/// <summary>The Resgrid Academy system corpus (lesson titles and topics), shared by every department (DepartmentId 0).</summary>
		Academy = 5,

		/// <summary>A Documents module file, only when the department opts in (setting 139) and Advanced Data Protection is off.</summary>
		Document = 6
	}

	/// <summary>Access recorded on a chunk at index time, beside its audience rules. Persisted: append only.</summary>
	[Flags]
	public enum KnowledgeAccessFlags
	{
		None = 0,

		/// <summary>Department administrators only (an admins-only document).</summary>
		AdminOnly = 1,

		/// <summary>Whoever works dispatch reads it whatever its audience (a Dispatch-kind protocol, as the library does).</summary>
		DispatchersRead = 2,

		/// <summary>Read by the item's assignees and managers only; the live readable set decides who that is (a training not open to all).</summary>
		AssigneesRead = 4,

		/// <summary>
		/// Not an access rule: the chunk is an approved translation (a locale variant) of its item's version (round 5F). It is read exactly like
		/// the source (same audience and flags); the retriever serves an item's variant instead of its source to askers of that language
		/// (<see cref="KnowledgeLocaleVariants"/>).
		/// </summary>
		LocaleVariant = 8
	}

	/// <summary>
	/// Locale variants in retrieval (round 5F): for each item, an asker whose language has an approved translation is answered from that
	/// translation's chunks (the exact locale first, then the same language); everyone else from the source chunks. An item never mixes
	/// languages in one answer, and a variant never widens who may read it (it carries the source's audience).
	/// </summary>
	public static class KnowledgeLocaleVariants
	{
		public static bool IsVariant(KnowledgeChunk chunk) => chunk != null && ((KnowledgeAccessFlags)chunk.AccessFlags).HasFlag(KnowledgeAccessFlags.LocaleVariant);

		public static List<T> Choose<T>(IEnumerable<T> items, Func<T, KnowledgeChunk> chunkOf, string locale)
		{
			var list = (items ?? Enumerable.Empty<T>()).Where(i => chunkOf(i) != null).ToList();
			if (!list.Any(i => IsVariant(chunkOf(i))))
				return list;

			var wanted = ProtocolTranslationRules.NormalizeLocale(locale);
			var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
			if (wanted != null)
				foreach (var group in list.Select(chunkOf).Where(IsVariant).GroupBy(c => c.ItemKey, StringComparer.Ordinal))
				{
					var exact = group.FirstOrDefault(c => string.Equals(c.Locale, wanted, StringComparison.OrdinalIgnoreCase));
					var language = exact ?? group.FirstOrDefault(c => ProtocolTranslationRules.SameLanguage(c.Locale, wanted));
					if (language != null)
						chosen[group.Key] = language.Locale;
				}

			return list.Where(i =>
			{
				var chunk = chunkOf(i);
				return chosen.TryGetValue(chunk.ItemKey, out var variantLocale)
					? IsVariant(chunk) && string.Equals(chunk.Locale, variantLocale, StringComparison.OrdinalIgnoreCase)
					: !IsVariant(chunk);
			}).ToList();
		}
	}

	/// <summary>Why a question landed in the knowledge gaps report (§16.4). Persisted: append only.</summary>
	public enum KnowledgeGapReasons
	{
		NotCovered = 0,
		ThumbsDown = 1
	}

	/// <summary>KnowledgeGaps.Status. Persisted: append only.</summary>
	public enum KnowledgeGapStatuses
	{
		Open = 0,
		Resolved = 1,
		Dismissed = 2
	}

	/// <summary>Where a question was asked. Persisted in KnowledgeGaps.Surface: append only.</summary>
	public enum KnowledgeAskSurfaces
	{
		Library = 0,
		Protocol = 1,
		Training = 2,
		Assistant = 3,
		Call = 4,
		Api = 5,

		/// <summary>A3: "How do I…?" from the Help menu's search, answered from the Resgrid Academy corpus only.</summary>
		Help = 6,

		/// <summary>K3: "Ask this course" in the course player, grounded in the course version the learner takes.</summary>
		Course = 7
	}

	/// <summary>The outcome of one knowledge question (also AiGenerations.Outcome).</summary>
	public static class KnowledgeOutcomes
	{
		/// <summary>A generated answer, every claim cited.</summary>
		public const string Answered = "Answered";

		/// <summary>Nothing adequate was found: "Your department's protocols and training don't cover this", with the nearest items.</summary>
		public const string NotCovered = "NotCovered";

		/// <summary>Sources only, no generated text (Advanced Data Protection keeps the question local, or the model output was refused).</summary>
		public const string Matched = "Matched";

		public const string NotEntitled = "NotEntitled";
		public const string Unconfigured = "Unconfigured";
		public const string Unavailable = "Unavailable";
		public const string BudgetExhausted = "BudgetExhausted";
		public const string Busy = "Busy";
		public const string Invalid = "Invalid";
	}

	/// <summary>How the sources of an answer were found.</summary>
	public static class KnowledgeRetrievalModes
	{
		/// <summary>Exact cosine over the embeddings merged with BM25 keyword scores.</summary>
		public const string Hybrid = "Hybrid";

		/// <summary>BM25 only: the embeddings service was unavailable, or the question was kept local (Advanced Data Protection).</summary>
		public const string KeywordOnly = "KeywordOnly";
	}

	/// <summary>
	/// One retrievable piece of department content (K1, M0368): where it came from (source kind, item, version, anchor and heading path),
	/// when it is in effect, who may read it (audience rules and access flags, the P1/T0 audience kinds) and its embedding, stored as
	/// little-endian float32 bytes beside the search index and scored exactly (cosine) after the department and audience filters
	/// (§25.4a item 11). DepartmentId 0 is the system corpus (the Academy).
	/// </summary>
	[Table("KnowledgeChunks")]
	public class KnowledgeChunk : IEntity
	{
		public const int ItemIdMaxLength = 64;
		public const int AnchorMaxLength = 128;
		public const int HeadingPathMaxLength = 500;
		public const int TitleMaxLength = 250;
		public const int AudienceMaxLength = 4000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public long KnowledgeChunkId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="KnowledgeSourceKinds"/>.</summary>
		public int SourceKind { get; set; }

		/// <summary>The item's id as text (protocol id, training id, Academy lesson id, document id).</summary>
		[MaxLength(ItemIdMaxLength)]
		public string ItemId { get; set; }

		/// <summary>The version the text came from (protocol version id, the training's last change, an attachment id).</summary>
		[MaxLength(ItemIdMaxLength)]
		public string VersionId { get; set; }

		/// <summary>The version as people read it ("2.1"); null when the item has no numbered versions.</summary>
		[MaxLength(32)]
		public string VersionLabel { get; set; }

		/// <summary>Order within the item.</summary>
		public int Ordinal { get; set; }

		/// <summary>The heading anchor the citation links to (the ids ProtocolContentDocument.InjectAnchors puts on headings), or page-N.</summary>
		[MaxLength(AnchorMaxLength)]
		public string Anchor { get; set; }

		/// <summary>The headings above the text, outermost first, joined with " › ".</summary>
		[MaxLength(HeadingPathMaxLength)]
		public string HeadingPath { get; set; }

		[MaxLength(TitleMaxLength)]
		public string Title { get; set; }

		public string Text { get; set; }

		/// <summary>SHA-256 (lower-case hex) of the text that was embedded; an unchanged hash is never embedded again.</summary>
		[MaxLength(64)]
		public string TextHash { get; set; }

		[MaxLength(16)]
		public string Locale { get; set; }

		public DateTime? EffectiveOn { get; set; }

		public DateTime? ExpiresOn { get; set; }

		/// <summary>Normalized audience rules (<see cref="ContentAudienceRule"/> list as JSON); empty = the whole department.</summary>
		[MaxLength(AudienceMaxLength)]
		public string AudienceJson { get; set; }

		/// <summary><see cref="KnowledgeAccessFlags"/>.</summary>
		public int AccessFlags { get; set; }

		/// <summary>The embedding, little-endian float32; null until worker 89 embeds it (an outage leaves it keyword-only).</summary>
		public byte[] Embedding { get; set; }

		[MaxLength(128)]
		public string EmbeddingModel { get; set; }

		public DateTime? EmbeddedOn { get; set; }

		public DateTime IndexedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return KnowledgeChunkId; }
			set { KnowledgeChunkId = Convert.ToInt64(value); }
		}

		[NotMapped]
		public string TableName => "KnowledgeChunks";

		[NotMapped]
		public string IdName => "KnowledgeChunkId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };

		/// <summary>The item key the diversity cap and the gaps report group by.</summary>
		[NotMapped]
		[JsonIgnore]
		public string ItemKey => SourceKind + ":" + ItemId;
	}

	/// <summary>
	/// The reindex queue (M0368): one row per item to rebuild, idempotent on (DepartmentId, SourceKind, ItemId). Publish and retire
	/// enqueue the item; worker 89 drains it. SourceKind <see cref="KnowledgeIndexRequest.DepartmentRebuild"/> asks for the whole department.
	/// </summary>
	[Table("KnowledgeIndexRequests")]
	public class KnowledgeIndexRequest : IEntity
	{
		/// <summary>The SourceKind of a whole-department rebuild request (ItemId empty).</summary>
		public const int DepartmentRebuild = -1;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int KnowledgeIndexRequestId { get; set; }

		public int DepartmentId { get; set; }

		public int SourceKind { get; set; }

		[MaxLength(KnowledgeChunk.ItemIdMaxLength)]
		public string ItemId { get; set; }

		public DateTime RequestedOn { get; set; }

		public int Attempts { get; set; }

		/// <summary>Bumped each time the item is asked for again: a request re-asked while it was being processed is not deleted with it.</summary>
		public int Revision { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return KnowledgeIndexRequestId; }
			set { KnowledgeIndexRequestId = (int)value; }
		}

		[NotMapped]
		public string TableName => "KnowledgeIndexRequests";

		[NotMapped]
		public string IdName => "KnowledgeIndexRequestId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A department's knowledge index (M0368): the revision readers cache against, whether it is active, and when the nightly repair
	/// pass is next due. DepartmentId 0 is the system corpus.
	/// </summary>
	[Table("KnowledgeIndexStates")]
	public class KnowledgeIndexState : IEntity
	{
		[Key]
		[Required]
		public int DepartmentId { get; set; }

		/// <summary>Bumped on every write to the department's chunks; the in-process corpus cache reloads when it changes.</summary>
		public long Revision { get; set; }

		/// <summary>The department was entitled when worker 89 last looked; an inactive index has no repair pass.</summary>
		public bool Active { get; set; }

		public DateTime? NextRepairOn { get; set; }

		public DateTime? LastRepairOn { get; set; }

		public DateTime? LastIndexedOn { get; set; }

		public int ChunkCount { get; set; }

		public int EmbeddedCount { get; set; }

		[MaxLength(128)]
		public string EmbeddingModel { get; set; }

		[MaxLength(500)]
		public string LastError { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentId; }
			set { DepartmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "KnowledgeIndexStates";

		[NotMapped]
		public string IdName => "DepartmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A question the department's content failed to answer (§16.4, M0369): "not covered", or a thumbs-down on an answer. Training officers
	/// and protocol owners see the report and turn a gap into a draft protocol change or a new training. While Advanced Data Protection is
	/// on, the question text is not kept (only the reason, the asker's role and the nearest sources).
	/// </summary>
	[Table("KnowledgeGaps")]
	public class KnowledgeGap : IEntity
	{
		public const int QuestionMaxLength = 2000;
		public const int AskerRoleMaxLength = 250;
		public const int NearestSourcesMaxLength = 4000;
		public const int ResolutionNoteMaxLength = 500;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int KnowledgeGapId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary>The asker's roles (names) when they asked.</summary>
		[MaxLength(AskerRoleMaxLength)]
		public string AskerRole { get; set; }

		[MaxLength(QuestionMaxLength)]
		public string Question { get; set; }

		/// <summary><see cref="KnowledgeGapReasons"/>.</summary>
		public int Reason { get; set; }

		/// <summary><see cref="KnowledgeAskSurfaces"/>.</summary>
		public int Surface { get; set; }

		/// <summary><see cref="KnowledgeGapStatuses"/>.</summary>
		public int Status { get; set; }

		public DateTime AskedOn { get; set; }

		/// <summary>The nearest sources (<see cref="KnowledgeGapSource"/> list as JSON).</summary>
		[MaxLength(NearestSourcesMaxLength)]
		public string NearestSourcesJson { get; set; }

		/// <summary>The item the question was asked from (<see cref="KnowledgeSourceKinds"/>), if any.</summary>
		public int? ContextKind { get; set; }

		[MaxLength(KnowledgeChunk.ItemIdMaxLength)]
		public string ContextItemId { get; set; }

		/// <summary>The logged exchange (AiGenerations.Id).</summary>
		[MaxLength(36)]
		public string AiGenerationId { get; set; }

		public DateTime? ResolvedOn { get; set; }

		public string ResolvedByUserId { get; set; }

		[MaxLength(ResolutionNoteMaxLength)]
		public string ResolutionNote { get; set; }

		[NotMapped]
		public List<KnowledgeGapSource> NearestSources
		{
			get
			{
				if (string.IsNullOrWhiteSpace(NearestSourcesJson))
					return new List<KnowledgeGapSource>();
				try
				{
					return JsonConvert.DeserializeObject<List<KnowledgeGapSource>>(NearestSourcesJson) ?? new List<KnowledgeGapSource>();
				}
				catch (JsonException)
				{
					return new List<KnowledgeGapSource>();
				}
			}
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return KnowledgeGapId; }
			set { KnowledgeGapId = (int)value; }
		}

		[NotMapped]
		public string TableName => "KnowledgeGaps";

		[NotMapped]
		public string IdName => "KnowledgeGapId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "NearestSources" };
	}

	/// <summary>A nearest source kept with a gap: enough to link to it and to start a draft from it.</summary>
	public sealed class KnowledgeGapSource
	{
		public int SourceKind { get; set; }
		public string ItemId { get; set; }
		public string VersionId { get; set; }
		public string VersionLabel { get; set; }
		public string Anchor { get; set; }
		public string Title { get; set; }
	}

	/// <summary>
	/// Department setting 139 (ProtoBuf), Knowledge → Settings (K1): whether the Documents module joins the department's knowledge corpus
	/// (decision 14: by opt-in) and the department's dosing tool, linked from any answer that quotes a dose. Missing = Documents not
	/// indexed, no dosing link.
	/// </summary>
	[ProtoContract]
	public class KnowledgeSettings
	{
		public const int DosingToolUrlMaxLength = 500;

		/// <summary>Index the Documents module (admins-only documents are never used for members who are not administrators).</summary>
		[ProtoMember(1)]
		public bool IncludeDocuments { get; set; }

		/// <summary>An https link to the department's dosing tool; answers quoting a dose link to it. Neither the model nor Resgrid computes doses.</summary>
		[ProtoMember(2)]
		public string DosingToolUrl { get; set; }

		public KnowledgeSettings Normalize()
		{
			var url = (DosingToolUrl ?? string.Empty).Trim();
			DosingToolUrl = url.Length > 0 && url.Length <= DosingToolUrlMaxLength && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
				? uri.ToString()
				: null;
			return this;
		}
	}

	/// <summary>
	/// Who is asking, as the calling surface knows them: the department, the user and the view claims of their session (a member without the
	/// Protocols, Training or Documents view claim retrieves nothing from that source).
	/// </summary>
	public sealed class KnowledgeReader
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string Locale { get; set; }
		public bool CanViewProtocols { get; set; }
		public bool CanViewTrainings { get; set; }
		public bool CanViewDocuments { get; set; }
	}

	/// <summary>
	/// What one reader may retrieve, built once per question before any scoring: the items of each source kind the reader may open right
	/// now (decided live by each source's own read rule), and the memberships the stored audience rules are tested against.
	/// </summary>
	public sealed class KnowledgeReaderScope
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public bool IsCurrentMember { get; set; }
		public bool IsDepartmentAdmin { get; set; }

		/// <summary>Authors, approvers and department admins: they read every protocol in the library.</summary>
		public bool IsProtocolManager { get; set; }

		public bool IsDispatcher { get; set; }

		public ContentAudienceMemberFacts Facts { get; set; }

		public Dictionary<KnowledgeSourceKinds, HashSet<string>> ReadableItems { get; set; } = new Dictionary<KnowledgeSourceKinds, HashSet<string>>();

		public void Allow(KnowledgeSourceKinds kind, IEnumerable<string> itemIds)
		{
			if (!ReadableItems.TryGetValue(kind, out var set))
			{
				set = new HashSet<string>(StringComparer.Ordinal);
				ReadableItems[kind] = set;
			}

			foreach (var id in itemIds ?? Enumerable.Empty<string>())
				if (!string.IsNullOrWhiteSpace(id))
					set.Add(id);
		}

		public static KnowledgeReaderScope Nobody(int departmentId) => new KnowledgeReaderScope { DepartmentId = departmentId };
	}

	/// <summary>
	/// The retrieval access rule (§16.3 "audience enforcement happens at retrieval"), pure. A chunk is retrievable when the reader is a
	/// current member, the chunk is the reader's department's (or the Academy system corpus), the item is in the reader's live readable set
	/// for its kind, and the access recorded on the chunk at index time allows the reader too, so a chunk whose stored audience is stale
	/// fails closed either way. Applied to every candidate before any scoring.
	/// </summary>
	public static class KnowledgeAccessFilter
	{
		public static bool CanRead(KnowledgeReaderScope scope, KnowledgeChunk chunk)
		{
			if (scope == null || chunk == null || !scope.IsCurrentMember || scope.DepartmentId <= 0 || string.IsNullOrWhiteSpace(chunk.ItemId))
				return false;

			if (!Enum.IsDefined(typeof(KnowledgeSourceKinds), chunk.SourceKind))
				return false;

			var kind = (KnowledgeSourceKinds)chunk.SourceKind;

			// The Academy is a system corpus for every member; nothing else is ever read from DepartmentId 0.
			if (kind == KnowledgeSourceKinds.Academy)
				return chunk.DepartmentId == 0;

			if (chunk.DepartmentId != scope.DepartmentId)
				return false;

			if (scope.ReadableItems == null || !scope.ReadableItems.TryGetValue(kind, out var readable) || readable == null || !readable.Contains(chunk.ItemId))
				return false;

			var flags = (KnowledgeAccessFlags)chunk.AccessFlags;
			if (flags.HasFlag(KnowledgeAccessFlags.AdminOnly) && !scope.IsDepartmentAdmin)
				return false;

			if (kind == KnowledgeSourceKinds.Protocol || kind == KnowledgeSourceKinds.ProtocolFile)
			{
				if (scope.IsProtocolManager)
					return true;
				if (flags.HasFlag(KnowledgeAccessFlags.DispatchersRead) && scope.IsDispatcher)
					return true;
			}

			// Assignment is decided by the live readable set (a training's assignees and managers).
			if (flags.HasFlag(KnowledgeAccessFlags.AssigneesRead))
				return true;

			var rules = ParseAudience(chunk.AudienceJson);
			if (rules.Count == 0)
				return true;

			var facts = scope.Facts;
			if (facts == null)
				return false;

			// Reading is a gate, not automation: a current member who is hidden still reads what is theirs (ProtocolAccessRules).
			var asCurrent = new ContentAudienceMemberFacts
			{
				UserId = facts.UserId ?? scope.UserId,
				IsActive = true,
				GroupIds = facts.GroupIds ?? new HashSet<int>(),
				RoleIds = facts.RoleIds ?? new HashSet<int>(),
				CertificationTypeIds = facts.CertificationTypeIds ?? new HashSet<int>(),
				UnitTypeIds = facts.UnitTypeIds ?? new HashSet<int>(),
				ShiftIds = facts.ShiftIds ?? new HashSet<int>()
			};
			return ContentAudienceMatcher.Matches(asCurrent, rules);
		}

		/// <summary>The stored audience; unreadable JSON is an audience nobody matches (never "everyone").</summary>
		public static List<ContentAudienceRule> ParseAudience(string audienceJson)
		{
			if (string.IsNullOrWhiteSpace(audienceJson))
				return new List<ContentAudienceRule>();
			try
			{
				var rules = JsonConvert.DeserializeObject<List<ContentAudienceRule>>(audienceJson);
				return rules == null ? new List<ContentAudienceRule>() : ContentAudienceMatcher.Normalize(rules);
			}
			catch (JsonException)
			{
				return new List<ContentAudienceRule> { new ContentAudienceRule(ContentAudienceKinds.User, "!unreadable") };
			}
		}

		/// <summary>The audience rules as stored on a chunk: normalized, null for the whole department.</summary>
		public static string SerializeAudience(IEnumerable<IContentAudienceRule> rules)
		{
			var normalized = ContentAudienceMatcher.Normalize(rules);
			if (normalized.Count == 0)
				return null;
			var json = JsonConvert.SerializeObject(normalized.Select(r => new { r.Kind, r.RefId }));
			// An audience too long to store is stored as nobody's rather than truncated into someone else's.
			return json.Length <= KnowledgeChunk.AudienceMaxLength ? json : JsonConvert.SerializeObject(new[] { new { Kind = (int)ContentAudienceKinds.User, RefId = "!overflow" } });
		}
	}

	/// <summary>One question to the department's knowledge (web Ask, the Assistant, the call view, v4 for Omni).</summary>
	public sealed class KnowledgeAskRequest
	{
		public const int QuestionMaxLength = 1000;

		public KnowledgeReader Reader { get; set; }

		public string Question { get; set; }

		/// <summary><see cref="KnowledgeAskSurfaces"/>.</summary>
		public int Surface { get; set; }

		/// <summary>Ask about one item only (a protocol detail page, a training page).</summary>
		public int? ScopeKind { get; set; }

		public string ScopeItemId { get; set; }

		/// <summary>
		/// K3: one version of the scoped item (the course version a learner takes in "Ask this course"); null lets the item's version source
		/// pick the reader's own (their enrollment, else the version in force).
		/// </summary>
		public string ScopeVersionId { get; set; }

		/// <summary>
		/// "Ask about this call's protocol": grounded in the version the call cites. K3: a call without <see cref="CallProtocolId"/> asks about
		/// the whole call: every protocol version it cites plus its operational fields (<see cref="KnowledgeCallContext"/>), never patient data.
		/// </summary>
		public int? CallId { get; set; }

		public int? CallProtocolId { get; set; }

		/// <summary>The versions the client holds offline (item key "kind:id" → version id); a cited item with a newer version is flagged stale.</summary>
		public Dictionary<string, string> OfflineVersions { get; set; }
	}

	public sealed class KnowledgeCitation
	{
		/// <summary>The id the answer refers to this source by ("S1").</summary>
		public string Ref { get; set; }
		public int SourceKind { get; set; }
		public string ItemId { get; set; }
		public string VersionId { get; set; }
		public string VersionLabel { get; set; }
		public string Anchor { get; set; }
		public string HeadingPath { get; set; }
		public string Title { get; set; }
		public DateTime? EffectiveOn { get; set; }
		/// <summary>A short quote of the cited text.</summary>
		public string Excerpt { get; set; }
		/// <summary>The client's offline copy of this item is older than the cited version.</summary>
		public bool OfflineCopyStale { get; set; }
	}

	/// <summary>One sentence or paragraph of a generated answer and the sources it rests on.</summary>
	public sealed class KnowledgeAnswerPart
	{
		public string Text { get; set; }
		public List<string> Refs { get; set; } = new List<string>();
	}

	/// <summary>Sources that disagree, shown together and labelled.</summary>
	public sealed class KnowledgeConflict
	{
		public string Note { get; set; }
		public List<string> Refs { get; set; } = new List<string>();
	}

	public sealed class KnowledgeAnswer
	{
		/// <summary><see cref="KnowledgeOutcomes"/>.</summary>
		public string Outcome { get; set; }

		public List<KnowledgeAnswerPart> Parts { get; set; } = new List<KnowledgeAnswerPart>();

		public List<KnowledgeCitation> Citations { get; set; } = new List<KnowledgeCitation>();

		/// <summary>The nearest items when the content does not cover the question.</summary>
		public List<KnowledgeCitation> Nearest { get; set; } = new List<KnowledgeCitation>();

		public List<KnowledgeConflict> Conflicts { get; set; } = new List<KnowledgeConflict>();

		/// <summary><see cref="KnowledgeRetrievalModes"/>.</summary>
		public string RetrievalMode { get; set; }

		/// <summary>The embeddings service did not answer in time; sources were found by keywords only.</summary>
		public bool EmbeddingsUnavailable { get; set; }

		/// <summary>Advanced Data Protection kept the question local: sources only, no generated text.</summary>
		public bool QuestionKeptLocal { get; set; }

		/// <summary>The answer quotes a dose: the department's dosing tool, when one is set (setting 139).</summary>
		public string DosingToolUrl { get; set; }

		public bool QuotesDose { get; set; }

		/// <summary>The client's offline copy of a cited item is older than the version cited.</summary>
		public bool OfflineCopyStale { get; set; }

		/// <summary>K3: the answer was grounded in a whole call (its cited versions and its operational fields).</summary>
		public bool CallContextUsed { get; set; }

		/// <summary>AiGenerations.Id of the logged exchange; feedback (thumbs up/down) names it.</summary>
		public string ExchangeId { get; set; }

		public string PromptVersion { get; set; }

		public string ModelRevision { get; set; }
	}

	/// <summary>Whether Ask is offered to this member right now, and why not.</summary>
	public sealed class KnowledgeStatus
	{
		public bool Available { get; set; }

		/// <summary><see cref="KnowledgeOutcomes"/> NotEntitled, Unconfigured or Unavailable when not available.</summary>
		public string Reason { get; set; }

		/// <summary>May see the knowledge gaps report (training officers and protocol owners).</summary>
		public bool CanSeeGaps { get; set; }

		public bool IsAdmin { get; set; }

		/// <summary>Advanced Data Protection keeps questions local in this department (sources only).</summary>
		public bool QuestionsKeptLocal { get; set; }
	}

	/// <summary>Extracted text of one page of a file (the page-text store; P2 fills it).</summary>
	public sealed class KnowledgePageText
	{
		/// <summary>The attachment the page belongs to (DispatchProtocolAttachmentId, TrainingAttachmentId, DocumentId).</summary>
		public string AttachmentId { get; set; }

		public string FileName { get; set; }

		public int PageNumber { get; set; }

		public string Text { get; set; }
	}

	/// <summary>What a corpus source hands the indexer for one item: its chunks, or nothing when the item is not indexable (excluded).</summary>
	public sealed class KnowledgeItemContent
	{
		public int DepartmentId { get; set; }
		public KnowledgeSourceKinds Kind { get; set; }
		public string ItemId { get; set; }
		public List<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();
	}

	/// <summary>
	/// Where a citation opens in the web app: the exact version and heading anchor of a protocol, the training page at its heading, the
	/// document, or the Academy lesson. Anchors hold only letters, numbers and dashes (ProtocolContentDocument), so they are safe in a fragment.
	/// </summary>
	public static class KnowledgeLinks
	{
		public static string WebPath(int sourceKind, string itemId, string versionId, string anchor, string academyUrl = null)
		{
			if (string.IsNullOrWhiteSpace(itemId))
				return null;
			var id = Uri.EscapeDataString(itemId);
			var fragment = SafeAnchor(anchor);
			switch ((KnowledgeSourceKinds)sourceKind)
			{
				case KnowledgeSourceKinds.Protocol:
				case KnowledgeSourceKinds.ProtocolFile:
					return "/User/Protocols/View/" + id + (int.TryParse(versionId, out var version) && version > 0 ? "?versionId=" + version : string.Empty) + fragment;
				case KnowledgeSourceKinds.Training:
				case KnowledgeSourceKinds.TrainingFile:
					return "/User/Trainings/View?trainingId=" + id + fragment;
				case KnowledgeSourceKinds.Document:
					return "/User/Documents/ViewDocument?documentId=" + id;
				case KnowledgeSourceKinds.Academy:
					return Academy.AcademyCatalog.LessonUrl(academyUrl, Academy.AcademyCatalog.Find(itemId));
				case KnowledgeSourceKinds.Course:
					// K3: the reader's own enrollment of the course opens at the lesson (Courses/Open), at the block inside it.
					var lesson = TryParseCourseAnchor(anchor, out var lessonId, out var inner) ? "?lesson=" + lessonId.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
					return "/User/Courses/Open/" + id + lesson + SafeAnchor(inner);
				default:
					return null;
			}
		}

		/// <summary>
		/// A course chunk's anchor (K3): the lesson it was cut from and the block or heading anchor inside that lesson's page
		/// ("l{lessonId}-b-{key}", "l{lessonId}-p-{slug}"), or the lesson alone ("l{lessonId}"). Letters, numbers and dashes only.
		/// </summary>
		public static string CourseAnchor(int lessonId, string inner)
		{
			if (lessonId <= 0)
				return null;
			var lesson = "l" + lessonId.ToString(System.Globalization.CultureInfo.InvariantCulture);
			var safe = SafeAnchor(inner);
			var anchor = safe.Length > 1 ? lesson + "-" + safe.Substring(1) : lesson;
			return anchor.Length <= KnowledgeChunk.AnchorMaxLength ? anchor : lesson;
		}

		/// <summary>Reads a <see cref="CourseAnchor"/>: the lesson id and the anchor inside the lesson (null for the lesson alone).</summary>
		public static bool TryParseCourseAnchor(string anchor, out int lessonId, out string inner)
		{
			lessonId = 0;
			inner = null;
			if (string.IsNullOrWhiteSpace(anchor) || anchor.Length < 2 || anchor[0] != 'l' || SafeAnchor(anchor).Length == 0)
				return false;
			var dash = anchor.IndexOf('-');
			var digits = dash < 0 ? anchor.Substring(1) : anchor.Substring(1, dash - 1);
			if (digits.Length == 0 || digits.Length > 9 || !int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out lessonId) || lessonId <= 0)
			{
				lessonId = 0;
				return false;
			}
			inner = dash < 0 || dash == anchor.Length - 1 ? null : anchor.Substring(dash + 1);
			return true;
		}

		/// <summary>The anchor as a URL fragment, or empty when it holds anything but letters, numbers and dashes.</summary>
		public static string SafeAnchor(string anchor)
		{
			if (string.IsNullOrWhiteSpace(anchor) || anchor.Length > KnowledgeChunk.AnchorMaxLength)
				return string.Empty;
			foreach (var c in anchor)
				if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-'))
					return string.Empty;
			return "#" + anchor;
		}
	}

	/// <summary>The stored form of an embedding: little-endian float32 bytes, identical on SQL Server (varbinary) and PostgreSQL (bytea).</summary>
	public static class KnowledgeVectorCodec
	{
		public static byte[] Encode(float[] vector)
		{
			if (vector == null || vector.Length == 0)
				return null;
			var bytes = new byte[vector.Length * 4];
			for (var i = 0; i < vector.Length; i++)
				System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), vector[i]);
			return bytes;
		}

		/// <summary>The vector, or null when the bytes are missing or not a whole number of floats.</summary>
		public static float[] Decode(byte[] bytes)
		{
			if (bytes == null || bytes.Length == 0 || bytes.Length % 4 != 0)
				return null;
			var vector = new float[bytes.Length / 4];
			for (var i = 0; i < vector.Length; i++)
				vector[i] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
			return vector;
		}
	}
}
