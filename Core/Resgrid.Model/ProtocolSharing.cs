using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>What a <see cref="ContentAttribution"/> row is attached to (ContentAttributions.SubjectKind). Persisted: append only.</summary>
	public enum ContentAttributionSubjects
	{
		/// <summary>A protocol version: attribution is kept on the version and carried to every later draft of the protocol.</summary>
		ProtocolVersion = 0,

		/// <summary>A baseline Training imported from the community exchange.</summary>
		Training = 1,

		/// <summary>An LMS skill sheet imported from the community exchange.</summary>
		SkillSheet = 2,

		/// <summary>An LMS lesson plan imported from the community exchange.</summary>
		LessonPlan = 3,

		/// <summary>An LMS course copied from a linked department's course set (round 10 A3): SubjectId is the subscriber's LmsCourseId.</summary>
		LmsCourse = 4,

		/// <summary>An LMS learning path copied from a linked department's course set (round 10 A3): SubjectId is the subscriber's LmsPathId.</summary>
		LmsPath = 5
	}

	/// <summary>Where attributed content came from (ContentAttributions.SourceKind). Persisted: append only.</summary>
	public enum ContentAttributionSources
	{
		/// <summary>A Resgrid template library starter (P5).</summary>
		TemplateLibrary = 0,

		/// <summary>A listing of the community exchange (P5, decision 7c).</summary>
		CommunityExchange = 1,

		/// <summary>An open-format file (version 2) that carried its own attribution.</summary>
		FileImport = 2,

		/// <summary>A publisher's protocol or course set (P5 / T5): SourceRef is the ContentSetId, SourceVersion the publisher's version label.</summary>
		ContentSet = 3
	}

	/// <summary>
	/// Attribution kept on content that came from outside the department (Protocols P5): the source, its licence and the source's own
	/// version. One row per subject (a protocol version, a training, a skill sheet or a lesson plan); a protocol's later drafts get a copy
	/// of the row of the version they were started from, so the attribution survives "import and modify". Never part of a content hash.
	/// </summary>
	[Table("ContentAttributions")]
	public class ContentAttribution : IEntity
	{
		public const int SourceNameMaxLength = 200;
		public const int SourceRefMaxLength = 100;
		public const int AuthorMaxLength = 200;
		public const int LicenseMaxLength = 50;
		public const int LicenseUrlMaxLength = 300;
		public const int SourceVersionMaxLength = 50;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ContentAttributionId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="ContentAttributionSubjects"/>.</summary>
		public int SubjectKind { get; set; }

		public int SubjectId { get; set; }

		/// <summary><see cref="ContentAttributionSources"/>.</summary>
		public int SourceKind { get; set; }

		/// <summary>The template set key or the exchange listing id the content came from.</summary>
		[MaxLength(SourceRefMaxLength)]
		public string SourceRef { get; set; }

		/// <summary>The source as people read it ("Resgrid template library: Fire SOG starter", "Community exchange").</summary>
		[MaxLength(SourceNameMaxLength)]
		public string SourceName { get; set; }

		/// <summary>Who made the content (the contributing department's attribution name, "Resgrid").</summary>
		[MaxLength(AuthorMaxLength)]
		public string Author { get; set; }

		/// <summary>An SPDX-style licence code (<see cref="ContentLicenses"/>), e.g. "CC-BY-4.0".</summary>
		[MaxLength(LicenseMaxLength)]
		public string License { get; set; }

		[MaxLength(LicenseUrlMaxLength)]
		public string LicenseUrl { get; set; }

		/// <summary>The source's own version label (the template set's version, the listing's version).</summary>
		[MaxLength(SourceVersionMaxLength)]
		public string SourceVersion { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		/// <summary>A copy of this attribution for another subject of the same department (a new draft started from an attributed version).</summary>
		public ContentAttribution CopyFor(ContentAttributionSubjects subject, int subjectId, string userId, DateTime nowUtc) => new ContentAttribution
		{
			DepartmentId = DepartmentId,
			SubjectKind = (int)subject,
			SubjectId = subjectId,
			SourceKind = SourceKind,
			SourceRef = SourceRef,
			SourceName = SourceName,
			Author = Author,
			License = License,
			LicenseUrl = LicenseUrl,
			SourceVersion = SourceVersion,
			CreatedOn = nowUtc,
			CreatedByUserId = userId
		};

		/// <summary>Trims every text to its column and drops a licence URL that is not http(s).</summary>
		public ContentAttribution Normalize()
		{
			SourceRef = Cut(SourceRef, SourceRefMaxLength);
			SourceName = Cut(SourceName, SourceNameMaxLength);
			Author = Cut(Author, AuthorMaxLength);
			License = Cut(License, LicenseMaxLength);
			LicenseUrl = ContentLicenses.SafeUrl(Cut(LicenseUrl, LicenseUrlMaxLength));
			SourceVersion = Cut(SourceVersion, SourceVersionMaxLength);
			return this;
		}

		private static string Cut(string value, int max)
		{
			var trimmed = value?.Trim();
			if (string.IsNullOrEmpty(trimmed))
				return null;
			return trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ContentAttributionId; }
			set { ContentAttributionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ContentAttributions";

		[NotMapped]
		public string IdName => "ContentAttributionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>The licences content may be shared under (template library and community exchange). Codes are SPDX identifiers.</summary>
	public static class ContentLicenses
	{
		public const string CcBy = "CC-BY-4.0";
		public const string CcBySa = "CC-BY-SA-4.0";
		public const string Cc0 = "CC0-1.0";

		/// <summary>The licences a department may contribute under, with their deed address.</summary>
		public static readonly IReadOnlyDictionary<string, string> Contributable = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[CcBy] = "https://creativecommons.org/licenses/by/4.0/",
			[CcBySa] = "https://creativecommons.org/licenses/by-sa/4.0/",
			[Cc0] = "https://creativecommons.org/publicdomain/zero/1.0/"
		};

		public static bool IsContributable(string code) => code != null && Contributable.ContainsKey(code);

		public static string UrlFor(string code) => code != null && Contributable.TryGetValue(code, out var url) ? url : null;

		/// <summary>An http(s) address, or null.</summary>
		public static string SafeUrl(string url)
		{
			if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
				return null;
			return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp ? uri.AbsoluteUri : null;
		}
	}

	/// <summary>What a public link shows (ProtocolPublicLinks.Kind). Persisted: append only.</summary>
	public enum ProtocolPublicLinkKinds
	{
		/// <summary>One protocol: its version in force (or one pinned version while it is in force).</summary>
		Protocol = 0,

		/// <summary>A category as a set: every eligible protocol in force in the category and its sub-categories.</summary>
		Category = 1
	}

	/// <summary>
	/// An opt-in public URL (Protocols P5, decision 6): one protocol's version in force, or a category as a set, served anonymously at
	/// /public/protocols/{token} with an optional embed route. Revocable; never shows drafts, audience-restricted or licensed content
	/// (<see cref="ProtocolPublicPages"/> decides on every request, so a protocol that later gains an audience or a licence flag drops
	/// out on its own).
	/// </summary>
	[Table("ProtocolPublicLinks")]
	public class ProtocolPublicLink : IEntity
	{
		public const int TokenLength = 32;
		public const int EmbedOriginsMaxLength = 1000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolPublicLinkId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="ProtocolPublicLinkKinds"/>.</summary>
		public int Kind { get; set; }

		public int? DispatchProtocolId { get; set; }

		public int? ProtocolCategoryId { get; set; }

		/// <summary>A protocol link pinned to one published version: shown while that version is in force, a "superseded" notice after.</summary>
		public int? ProtocolVersionId { get; set; }

		/// <summary>The unguessable URL token (<see cref="ProtocolPublicPages.NewToken"/>, 32 url-safe characters).</summary>
		[MaxLength(TokenLength)]
		public string Token { get; set; }

		/// <summary>Whether the /embed route answers (with frame-ancestors from <see cref="EmbedOrigins"/>).</summary>
		public bool EmbedEnabled { get; set; }

		/// <summary>The https origins allowed to frame the embed route, space separated; blank = any https site.</summary>
		[MaxLength(EmbedOriginsMaxLength)]
		public string EmbedOrigins { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? RevokedOn { get; set; }

		public string RevokedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsRevoked => RevokedOn.HasValue;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolPublicLinkId; }
			set { ProtocolPublicLinkId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolPublicLinks";

		[NotMapped]
		public string IdName => "ProtocolPublicLinkId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsRevoked" };
	}

	/// <summary>Why a public link was refused (each maps to a localized message).</summary>
	public enum ProtocolPublicLinkErrors
	{
		None = 0,
		NotAuthorized,
		NotFound,
		/// <summary>Not published, licensed content, audience-restricted or document-backed: <see cref="ProtocolPublicPages.Eligibility"/>.</summary>
		NotEligible,
		/// <summary>A protocol already has a live link of the same shape (one per protocol, or per pinned version).</summary>
		AlreadyPublic,
		InvalidOrigins,
		/// <summary>The department already has the most live links one department may hold.</summary>
		TooManyLinks
	}

	/// <summary>Why one protocol cannot be on a public page (shown to the approver; never to the public).</summary>
	public enum ProtocolPublicEligibility
	{
		Eligible = 0,
		NotPublished,
		LicensedContent,
		AudienceRestricted,
		DocumentBacked,
		Disabled
	}

	/// <summary>A link with what it points at, for the department's list.</summary>
	public sealed class ProtocolPublicLinkView
	{
		public ProtocolPublicLink Link { get; set; }
		public string Title { get; set; }
		public string VersionLabel { get; set; }
		public ProtocolPublicEligibility Eligibility { get; set; }
		/// <summary>For a category set: how many of its protocols are on the page now.</summary>
		public int ShownCount { get; set; }
		public string CreatedByName { get; set; }
	}

	public sealed class ProtocolPublicLinkResult
	{
		public ProtocolPublicLinkErrors Error { get; set; }
		public ProtocolPublicEligibility Eligibility { get; set; }
		public ProtocolPublicLink Link { get; set; }
		public bool Succeeded => Error == ProtocolPublicLinkErrors.None;

		public static ProtocolPublicLinkResult Fail(ProtocolPublicLinkErrors error, ProtocolPublicEligibility eligibility = ProtocolPublicEligibility.Eligible) =>
			new ProtocolPublicLinkResult { Error = error, Eligibility = eligibility };
	}

	/// <summary>
	/// One protocol as the public sees it. These are the only fields a public page carries (a guard test pins the list): never the
	/// owner, the audience, approvers, review dates, triggers, key questions, the "no AI" flag, the licence flag or any id.
	/// </summary>
	public sealed class PublicProtocolEntry
	{
		public string Title { get; set; }
		public string Number { get; set; }
		public string KindName { get; set; }
		public string CategoryName { get; set; }
		public string VersionLabel { get; set; }
		public DateTime? EffectiveOn { get; set; }
		/// <summary>The version's (or its approved translation's) text, sanitized for output; the page writes it through the protocol content helper.</summary>
		public string BodyHtml { get; set; }
		/// <summary>The content language shown.</summary>
		public string Locale { get; set; }
		/// <summary>"Source · licence · version" kept from the template library or the exchange; null for the department's own content.</summary>
		public PublicProtocolAttribution Attribution { get; set; }
		/// <summary>The content hash's short code (as on the PDF book), so a printout can be matched to the page.</summary>
		public string ContentShortCode { get; set; }
	}

	public sealed class PublicProtocolAttribution
	{
		public string SourceName { get; set; }
		public string Author { get; set; }
		public string License { get; set; }
		public string LicenseUrl { get; set; }
		public string SourceVersion { get; set; }
	}

	/// <summary>The public page (one protocol, or a category set).</summary>
	public sealed class PublicProtocolPage
	{
		public string DepartmentName { get; set; }
		public string Title { get; set; }
		public bool IsSet { get; set; }
		/// <summary>A pinned version that is no longer in force: the page says so and shows nothing else.</summary>
		public bool Superseded { get; set; }
		public DateTime GeneratedOn { get; set; }
		public List<PublicProtocolEntry> Entries { get; set; } = new List<PublicProtocolEntry>();
	}

	/// <summary>The embed route's framing policy for a link.</summary>
	public sealed class PublicProtocolEmbedPolicy
	{
		public bool Enabled { get; set; }
		/// <summary>The frame-ancestors source list ("https:" for any https site).</summary>
		public string FrameAncestors { get; set; }
	}

	/// <summary>
	/// The rules every public page applies (P5, decision 6): opt-in per protocol or category, never drafts, audiences or licensed content,
	/// served anonymously under a strict CSP, framed only through the embed route.
	/// </summary>
	public static class ProtocolPublicPages
	{
		private static readonly Regex TokenShape = new Regex("^[A-Za-z0-9_-]{" + ProtocolPublicLink.TokenLength + "}$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
		private static readonly Regex OriginShape = new Regex(@"^https://[a-z0-9.-]+(:[0-9]{1,5})?$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
		public const int MaxEmbedOrigins = 10;

		/// <summary>The strict policy of the page itself: this site's styles only, no scripts, no frames, framed by nobody.</summary>
		public const string PageCsp = "default-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' https: data:; font-src 'self'; " +
			"frame-ancestors 'none'; form-action 'none'; base-uri 'none'; object-src 'none'";

		/// <summary>The embed route's policy: the same, framed by the link's allowed origins.</summary>
		public static string EmbedCsp(string frameAncestors) => "default-src 'none'; style-src 'self' 'unsafe-inline'; img-src 'self' https: data:; font-src 'self'; " +
			"frame-ancestors " + (string.IsNullOrWhiteSpace(frameAncestors) ? "'none'" : frameAncestors) + "; form-action 'none'; base-uri 'none'; object-src 'none'";

		/// <summary>A new token: 24 random bytes, base64url (32 characters).</summary>
		public static string NewToken()
		{
			var bytes = RandomNumberGenerator.GetBytes(24);
			return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
		}

		public static bool IsTokenShape(string token) => !string.IsNullOrEmpty(token) && TokenShape.IsMatch(token);

		/// <summary>
		/// The allowed embed origins as a normalized space-separated list (lower-case https origins, no paths, at most
		/// <see cref="MaxEmbedOrigins"/>), or null when <paramref name="input"/> holds something that is not an https origin.
		/// Blank input gives blank (any https site).
		/// </summary>
		public static string NormalizeEmbedOrigins(string input, out bool valid)
		{
			valid = true;
			if (string.IsNullOrWhiteSpace(input))
				return string.Empty;

			var origins = new List<string>();
			foreach (var raw in input.Split(new[] { ' ', ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var candidate = raw.Trim().TrimEnd('/').ToLowerInvariant();
				if (!OriginShape.IsMatch(candidate))
				{
					valid = false;
					return null;
				}
				if (!origins.Contains(candidate))
					origins.Add(candidate);
			}

			if (origins.Count > MaxEmbedOrigins)
			{
				valid = false;
				return null;
			}

			var joined = string.Join(" ", origins);
			if (joined.Length > ProtocolPublicLink.EmbedOriginsMaxLength)
			{
				valid = false;
				return null;
			}
			return joined;
		}

		/// <summary>The embed route's policy for a link: off unless enabled; the listed origins, else any https site.</summary>
		public static PublicProtocolEmbedPolicy EmbedPolicy(ProtocolPublicLink link)
		{
			if (link == null || link.IsRevoked || !link.EmbedEnabled)
				return new PublicProtocolEmbedPolicy { Enabled = false, FrameAncestors = "'none'" };
			var origins = NormalizeEmbedOrigins(link.EmbedOrigins, out var valid);
			return new PublicProtocolEmbedPolicy { Enabled = true, FrameAncestors = valid && !string.IsNullOrEmpty(origins) ? origins : "https:" };
		}

		/// <summary>
		/// Whether a protocol may be on a public page right now (decision 6): published and in force, not disabled, not licensed content
		/// (<see cref="ProtocolLicensing.CanPublishPublicly"/>), no audience rows (an audience-restricted protocol is never public) and an
		/// authored, not document-backed, version in force.
		/// </summary>
		public static ProtocolPublicEligibility Eligibility(DispatchProtocol protocol, ProtocolVersion current, IEnumerable<IContentAudienceRule> audience)
		{
			if (protocol == null || protocol.IsDeleted || !protocol.IsPublished || current == null)
				return ProtocolPublicEligibility.NotPublished;
			if (protocol.IsLicensedContent)
				return ProtocolPublicEligibility.LicensedContent;
			if (!ProtocolLicensing.CanPublishPublicly(protocol))
				return ProtocolPublicEligibility.NotPublished;
			if (protocol.IsDisabled)
				return ProtocolPublicEligibility.Disabled;
			if (current.DispatchProtocolId != protocol.DispatchProtocolId || current.DepartmentId != protocol.DepartmentId ||
				current.Status != (int)ProtocolVersionStatuses.Published || protocol.CurrentVersionId != current.ProtocolVersionId)
				return ProtocolPublicEligibility.NotPublished;
			if ((audience ?? Enumerable.Empty<IContentAudienceRule>()).Any(r => r != null && r.Kind != (int)ContentAudienceKinds.Everyone))
				return ProtocolPublicEligibility.AudienceRestricted;
			if (current.IsDocument)
				return ProtocolPublicEligibility.DocumentBacked;
			return ProtocolPublicEligibility.Eligible;
		}

		/// <summary>
		/// The public projection of one eligible protocol: only the fields of <see cref="PublicProtocolEntry"/>. Null when the protocol is not
		/// eligible (callers never get a page for it). <paramref name="displayHtml"/> is the sanitized text to show (the version's, or its
		/// approved translation's).
		/// </summary>
		public static PublicProtocolEntry Project(DispatchProtocol protocol, ProtocolVersion current, IEnumerable<IContentAudienceRule> audience, string categoryName,
			string kindName, string displayHtml, string locale, ContentAttribution attribution)
		{
			if (Eligibility(protocol, current, audience) != ProtocolPublicEligibility.Eligible)
				return null;

			return new PublicProtocolEntry
			{
				Title = protocol.Name,
				Number = string.IsNullOrWhiteSpace(protocol.Number) ? protocol.Code : protocol.Number,
				KindName = kindName,
				CategoryName = categoryName,
				VersionLabel = current.Label,
				EffectiveOn = current.EffectiveOn,
				BodyHtml = displayHtml ?? string.Empty,
				Locale = locale,
				ContentShortCode = SignaturePolicy.ShortCode(current.ContentHash),
				Attribution = attribution == null ? null : new PublicProtocolAttribution
				{
					SourceName = attribution.SourceName,
					Author = attribution.Author,
					License = attribution.License,
					LicenseUrl = ContentLicenses.SafeUrl(attribution.LicenseUrl),
					SourceVersion = attribution.SourceVersion
				}
			};
		}

		/// <summary>The embed snippet a department pastes into its site (an iframe of the embed route; nothing else).</summary>
		public static string EmbedSnippet(string embedUrl, string title) =>
			"<iframe src=\"" + System.Net.WebUtility.HtmlEncode(embedUrl ?? string.Empty) + "\" title=\"" + System.Net.WebUtility.HtmlEncode(title ?? string.Empty) +
			"\" style=\"width:100%;min-height:600px;border:0\" loading=\"lazy\" referrerpolicy=\"no-referrer\" sandbox=\"allow-popups allow-popups-to-escape-sandbox\"></iframe>";
	}

	/// <summary>Where a zero-result protocol search was made (ProtocolSearchMisses.Surface). Persisted: append only.</summary>
	public enum ProtocolSearchSurfaces
	{
		/// <summary>The protocol library's search box.</summary>
		Library = 0,

		/// <summary>Unified Search with the protocol filter.</summary>
		UnifiedSearch = 1
	}

	/// <summary>
	/// A protocol search that found nothing (P5 analytics, "protocol gaps"): one row per department, surface, normalized query and day,
	/// counted. Never names the person who searched.
	/// </summary>
	[Table("ProtocolSearchMisses")]
	public class ProtocolSearchMiss : IEntity
	{
		public const int QueryMaxLength = 200;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ProtocolSearchMissId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="ProtocolSearchSurfaces"/>.</summary>
		public int Surface { get; set; }

		/// <summary>The normalized query (<see cref="ProtocolAnalytics.NormalizeQuery"/>).</summary>
		[MaxLength(QueryMaxLength)]
		public string Query { get; set; }

		/// <summary>The UTC day the misses were counted for.</summary>
		public DateTime Day { get; set; }

		public int Count { get; set; }

		public DateTime LastSeenOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ProtocolSearchMissId; }
			set { ProtocolSearchMissId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ProtocolSearchMisses";

		[NotMapped]
		public string IdName => "ProtocolSearchMissId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One call-protocol row as the analytics read it (CallProtocols joined to Calls; the step log for time on protocol).</summary>
	public sealed class ProtocolCallUsageRow
	{
		public int CallProtocolId { get; set; }
		public int CallId { get; set; }
		public int DispatchProtocolId { get; set; }
		public int? ProtocolVersionId { get; set; }
		public int? Source { get; set; }
		public int Trigger { get; set; }
		public string Determinant { get; set; }
		public string OverrideReason { get; set; }
		public DateTime? AttachedOn { get; set; }
		public DateTime? RemovedOn { get; set; }
		public DateTime LoggedOn { get; set; }
		public string StepLogJson { get; set; }
	}

	/// <summary>A search query that found nothing, summed over the period.</summary>
	public sealed class ProtocolSearchMissSummary
	{
		public string Query { get; set; }
		public int Surface { get; set; }
		public int Count { get; set; }
		public DateTime LastSeenOn { get; set; }
	}

	public sealed class ProtocolVersionUsage
	{
		public int? ProtocolVersionId { get; set; }
		public string Label { get; set; }
		public int Calls { get; set; }
	}

	public sealed class ProtocolDeterminantCount
	{
		public string Determinant { get; set; }
		public int Calls { get; set; }
		public int Overridden { get; set; }
	}

	/// <summary>One protocol's line of the analytics dashboard (and of its CSV).</summary>
	public sealed class ProtocolAnalyticsRow
	{
		public int DispatchProtocolId { get; set; }
		public string Name { get; set; }
		public string Number { get; set; }
		public int Kind { get; set; }

		/// <summary>Calls the protocol was attached to in the period (removed attachments excluded).</summary>
		public int Calls { get; set; }
		public int ByTrigger { get; set; }
		public int ByDispatcher { get; set; }
		public int ByAi { get; set; }
		/// <summary>Attachments removed from their call (wrong protocol) in the period.</summary>
		public int Removed { get; set; }
		public List<ProtocolVersionUsage> Versions { get; set; } = new List<ProtocolVersionUsage>();

		/// <summary>Calls with a determinant (P4 case flow) and how many of those the dispatcher overrode.</summary>
		public int WithDeterminant { get; set; }
		public int Overridden { get; set; }
		public decimal? OverrideRate { get; set; }
		public List<ProtocolDeterminantCount> Determinants { get; set; } = new List<ProtocolDeterminantCount>();

		/// <summary>Step-through logs with a start and an end in the period, and their median / mean minutes.</summary>
		public int TimedCalls { get; set; }
		public decimal? MedianMinutes { get; set; }
		public decimal? MeanMinutes { get; set; }

		/// <summary>P2: acknowledgments owed on the version in force (withdrawn and superseded rows excluded), signed, overdue.</summary>
		public int AckAssigned { get; set; }
		public int AckSigned { get; set; }
		public int AckOverdue { get; set; }
		public decimal? AckCompliance { get; set; }
	}

	public sealed class ProtocolAnalyticsReport
	{
		public int DepartmentId { get; set; }
		public DateTime FromUtc { get; set; }
		public DateTime ToUtc { get; set; }
		public DateTime GeneratedOn { get; set; }
		public List<ProtocolAnalyticsRow> Rows { get; set; } = new List<ProtocolAnalyticsRow>();

		public int TotalCalls { get; set; }
		public int TotalWithDeterminant { get; set; }
		public int TotalOverridden { get; set; }
		public decimal? OverrideRate { get; set; }
		public decimal? MedianMinutes { get; set; }
		public int AckAssigned { get; set; }
		public int AckSigned { get; set; }
		public decimal? AckCompliance { get; set; }

		/// <summary>The most frequent determinants across protocols.</summary>
		public List<ProtocolDeterminantCount> Determinants { get; set; } = new List<ProtocolDeterminantCount>();

		/// <summary>Library and Unified Search queries that found no protocol (protocol gaps).</summary>
		public List<ProtocolSearchMissSummary> SearchMisses { get; set; } = new List<ProtocolSearchMissSummary>();

		/// <summary>Open K1 knowledge gaps asked from the library or a protocol (questions the protocols did not answer).</summary>
		public int OpenKnowledgeGaps { get; set; }

		/// <summary>True when the period held more call rows than one report reads (<see cref="ProtocolAnalytics.MaxCallRows"/>).</summary>
		public bool Truncated { get; set; }
	}

	/// <summary>
	/// The analytics maths (P5), pure so tests pin it: usage by protocol and version, determinant distribution and override rate (P4 case
	/// data), time on protocol from the step log, acknowledgment compliance (P2) and the normalized form of a missed search.
	/// </summary>
	public static class ProtocolAnalytics
	{
		public const int MaxCallRows = 50000;
		public const int MaxPeriodDays = 366;
		public const int TopDeterminants = 15;
		public const int TopSearchMisses = 25;

		/// <summary>A step log longer than this (12 hours) is a log left open, not time on protocol; it is not counted.</summary>
		public static readonly TimeSpan MaxTimeOnProtocol = TimeSpan.FromHours(12);

		private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		/// <summary>Lower-case, trimmed, single-spaced, at most <see cref="ProtocolSearchMiss.QueryMaxLength"/>; null for a blank query.</summary>
		public static string NormalizeQuery(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
				return null;
			var normalized = Spaces.Replace(query.Trim(), " ").ToLowerInvariant();
			if (normalized.Length < 2)
				return null;
			return normalized.Length > ProtocolSearchMiss.QueryMaxLength ? normalized.Substring(0, ProtocolSearchMiss.QueryMaxLength) : normalized;
		}

		/// <summary>The rate as a fraction 0..1 rounded to 4 places; null when there is nothing to divide by.</summary>
		public static decimal? Rate(int part, int whole) => whole <= 0 ? (decimal?)null : Math.Round((decimal)part / whole, 4, MidpointRounding.AwayFromZero);

		public static decimal? Median(IReadOnlyList<double> values)
		{
			if (values == null || values.Count == 0)
				return null;
			var sorted = values.OrderBy(v => v).ToList();
			var middle = sorted.Count / 2;
			var median = sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2d;
			return Math.Round((decimal)median, 2, MidpointRounding.AwayFromZero);
		}

		public static decimal? Mean(IReadOnlyList<double> values) =>
			values == null || values.Count == 0 ? (decimal?)null : Math.Round((decimal)values.Average(), 2, MidpointRounding.AwayFromZero);

		/// <summary>
		/// Minutes on protocol for one call: from the step log's start to its completion (or its last entry when it was never marked
		/// complete); null without a usable log or for a span that is negative or longer than <see cref="MaxTimeOnProtocol"/>.
		/// </summary>
		public static double? MinutesOnProtocol(string stepLogJson)
		{
			if (string.IsNullOrWhiteSpace(stepLogJson))
				return null;
			var log = ProtocolStepLog.Parse(stepLogJson);
			if (log == null || log.StartedOn == default)
				return null;
			var end = log.CompletedOn ?? log.Entries?.Where(e => e != null).Select(e => (DateTime?)e.At).Max();
			if (!end.HasValue)
				return null;
			var span = end.Value - log.StartedOn;
			if (span < TimeSpan.Zero || span > MaxTimeOnProtocol)
				return null;
			return span.TotalMinutes;
		}

		/// <summary>Builds the per-protocol rows and the department totals from the rows read for the period.</summary>
		public static ProtocolAnalyticsReport Build(int departmentId, DateTime fromUtc, DateTime toUtc, IEnumerable<DispatchProtocol> protocols,
			IEnumerable<ProtocolVersion> versions, IEnumerable<ProtocolCallUsageRow> calls, IEnumerable<ProtocolAcknowledgment> acknowledgments,
			DateTime utcNow)
		{
			var report = new ProtocolAnalyticsReport { DepartmentId = departmentId, FromUtc = fromUtc, ToUtc = toUtc, GeneratedOn = utcNow };
			var protocolList = (protocols ?? Enumerable.Empty<DispatchProtocol>()).Where(p => p != null && p.DepartmentId == departmentId && !p.IsDeleted).ToList();
			var versionById = (versions ?? Enumerable.Empty<ProtocolVersion>()).Where(v => v != null).GroupBy(v => v.ProtocolVersionId).ToDictionary(g => g.Key, g => g.First());
			var callRows = (calls ?? Enumerable.Empty<ProtocolCallUsageRow>()).Where(c => c != null).ToList();
			var byProtocol = callRows.ToLookup(c => c.DispatchProtocolId);
			var ackByProtocol = (acknowledgments ?? Enumerable.Empty<ProtocolAcknowledgment>()).Where(a => a != null && a.DepartmentId == departmentId).ToLookup(a => a.DispatchProtocolId);
			var allMinutes = new List<double>();

			foreach (var protocol in protocolList)
			{
				var rows = byProtocol[protocol.DispatchProtocolId].ToList();
				var live = rows.Where(r => !r.RemovedOn.HasValue).ToList();
				var row = new ProtocolAnalyticsRow
				{
					DispatchProtocolId = protocol.DispatchProtocolId,
					Name = protocol.Name,
					Number = string.IsNullOrWhiteSpace(protocol.Number) ? protocol.Code : protocol.Number,
					Kind = protocol.Kind,
					Calls = live.Select(r => r.CallId).Distinct().Count(),
					Removed = rows.Count(r => r.RemovedOn.HasValue),
					ByTrigger = live.Count(r => (r.Source ?? (int)CallProtocolSources.Trigger) == (int)CallProtocolSources.Trigger),
					ByDispatcher = live.Count(r => r.Source == (int)CallProtocolSources.Manual),
					ByAi = live.Count(r => r.Source == (int)CallProtocolSources.Ai)
				};

				row.Versions = live.GroupBy(r => r.ProtocolVersionId)
					.Select(g => new ProtocolVersionUsage
					{
						ProtocolVersionId = g.Key,
						Label = g.Key.HasValue && versionById.TryGetValue(g.Key.Value, out var version) ? version.Label : null,
						Calls = g.Select(r => r.CallId).Distinct().Count()
					})
					.OrderByDescending(v => v.Calls).ThenByDescending(v => v.ProtocolVersionId ?? 0).ToList();

				var determined = live.Where(r => !string.IsNullOrWhiteSpace(r.Determinant)).ToList();
				row.WithDeterminant = determined.Count;
				row.Overridden = determined.Count(r => !string.IsNullOrWhiteSpace(r.OverrideReason));
				row.OverrideRate = Rate(row.Overridden, row.WithDeterminant);
				row.Determinants = Determinants(determined);

				var minutes = live.Select(r => MinutesOnProtocol(r.StepLogJson)).Where(m => m.HasValue).Select(m => m.Value).ToList();
				row.TimedCalls = minutes.Count;
				row.MedianMinutes = Median(minutes);
				row.MeanMinutes = Mean(minutes);
				allMinutes.AddRange(minutes);

				// Compliance on the version in force: rows owed (open, signed, awaiting the quiz); withdrawn and superseded rows owe nothing.
				var acks = protocol.CurrentVersionId.HasValue
					? ackByProtocol[protocol.DispatchProtocolId].Where(a => a.ProtocolVersionId == protocol.CurrentVersionId.Value).ToList()
					: new List<ProtocolAcknowledgment>();
				var owed = acks.Where(a => a.Status == (int)ProtocolAcknowledgmentStatuses.Open || a.Status == (int)ProtocolAcknowledgmentStatuses.Acknowledged ||
					a.Status == (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz).ToList();
				row.AckAssigned = owed.Count;
				row.AckSigned = owed.Count(a => a.Status == (int)ProtocolAcknowledgmentStatuses.Acknowledged);
				row.AckOverdue = owed.Count(a => a.Status != (int)ProtocolAcknowledgmentStatuses.Acknowledged && a.DueOn.HasValue && a.DueOn.Value < utcNow);
				row.AckCompliance = Rate(row.AckSigned, row.AckAssigned);

				report.Rows.Add(row);
			}

			report.Rows = report.Rows.OrderByDescending(r => r.Calls).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
			report.TotalCalls = callRows.Where(r => !r.RemovedOn.HasValue).Select(r => r.CallId).Distinct().Count();
			report.TotalWithDeterminant = report.Rows.Sum(r => r.WithDeterminant);
			report.TotalOverridden = report.Rows.Sum(r => r.Overridden);
			report.OverrideRate = Rate(report.TotalOverridden, report.TotalWithDeterminant);
			report.MedianMinutes = Median(allMinutes);
			report.AckAssigned = report.Rows.Sum(r => r.AckAssigned);
			report.AckSigned = report.Rows.Sum(r => r.AckSigned);
			report.AckCompliance = Rate(report.AckSigned, report.AckAssigned);
			report.Determinants = Determinants(callRows.Where(r => !r.RemovedOn.HasValue && !string.IsNullOrWhiteSpace(r.Determinant)));
			return report;
		}

		private static List<ProtocolDeterminantCount> Determinants(IEnumerable<ProtocolCallUsageRow> rows) =>
			rows.GroupBy(r => r.Determinant.Trim().ToUpperInvariant())
				.Select(g => new ProtocolDeterminantCount { Determinant = g.Key, Calls = g.Count(), Overridden = g.Count(r => !string.IsNullOrWhiteSpace(r.OverrideReason)) })
				.OrderByDescending(d => d.Calls).ThenBy(d => d.Determinant, StringComparer.Ordinal)
				.Take(TopDeterminants).ToList();

		/// <summary>The dashboard as CSV (one line per protocol), invariant culture, every text cell quoted and formula-guarded.</summary>
		public static string ToCsv(ProtocolAnalyticsReport report, IReadOnlyList<string> headers)
		{
			var csv = new System.Text.StringBuilder();
			csv.AppendLine(string.Join(",", headers.Select(Cell)));
			foreach (var row in report?.Rows ?? new List<ProtocolAnalyticsRow>())
			{
				csv.AppendLine(string.Join(",", new[]
				{
					Cell(row.Number), Cell(row.Name), Num(row.Calls), Num(row.ByTrigger), Num(row.ByDispatcher), Num(row.ByAi), Num(row.Removed),
					Cell(string.Join(" ", row.Versions.Select(v => (v.Label ?? "-") + ":" + v.Calls.ToString(CultureInfo.InvariantCulture)))),
					Num(row.WithDeterminant), Num(row.Overridden), Dec(row.OverrideRate),
					Cell(string.Join(" ", row.Determinants.Select(d => d.Determinant + ":" + d.Calls.ToString(CultureInfo.InvariantCulture)))),
					Num(row.TimedCalls), Dec(row.MedianMinutes), Dec(row.MeanMinutes),
					Num(row.AckAssigned), Num(row.AckSigned), Num(row.AckOverdue), Dec(row.AckCompliance)
				}));
			}
			return csv.ToString();
		}

		private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

		private static string Dec(decimal? value) => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;

		/// <summary>A quoted CSV cell; a value a spreadsheet would run as a formula gets a leading apostrophe.</summary>
		public static string Cell(string value)
		{
			var text = value ?? string.Empty;
			if (text.Length > 0 && "=+-@\t\r".IndexOf(text[0]) >= 0)
				text = "'" + text;
			return "\"" + text.Replace("\"", "\"\"") + "\"";
		}
	}
}
