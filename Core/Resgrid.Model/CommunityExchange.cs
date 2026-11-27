using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Resgrid.Framework;

namespace Resgrid.Model
{
	/// <summary>What a community exchange listing holds (ExchangeListings.ItemKind). Persisted: append only.</summary>
	public enum ExchangeItemKinds
	{
		/// <summary>A protocol (any kind but a bulletin), as an open-format document of one item.</summary>
		Protocol = 0,

		/// <summary>A baseline Training with its quiz (free).</summary>
		Training = 1,

		/// <summary>An LMS skill sheet: imported as its own type, which needs the LMS add-on to use (decision 7c, §13a.4).</summary>
		SkillSheet = 2,

		/// <summary>An LMS lesson plan: imported as its own type, which needs the LMS add-on to use.</summary>
		LessonPlan = 3
	}

	/// <summary>Where a listing stands (ExchangeListings.Status). Persisted: append only.</summary>
	public enum ExchangeListingStatuses
	{
		/// <summary>Submitted; only the contributor and Resgrid's reviewers see it (decision 5: reviewed before listing).</summary>
		PendingReview = 0,

		/// <summary>Reviewed and listed: every department can browse, rate, comment and import it.</summary>
		Listed = 1,

		/// <summary>Declined by the reviewer, with a note to the contributor.</summary>
		Rejected = 2,

		/// <summary>Taken back by the contributing department.</summary>
		Withdrawn = 3,

		/// <summary>Taken down by a reviewer after listing.</summary>
		Removed = 4
	}

	/// <summary>
	/// A community exchange listing (Protocols P5 / Training T5, §13a row 42, decision 7c — free for every department): a department's
	/// protocol, training, skill sheet or lesson plan, stripped of department-specific data, listed only after a Resgrid reviewer passes
	/// it. Licensed content can never be submitted. The payload is a self-contained copy; imports make their own copies with attribution.
	/// </summary>
	[Table("ExchangeListings")]
	public class ExchangeListing : IEntity
	{
		public const int TitleMaxLength = 200;
		public const int SummaryMaxLength = 2000;
		public const int TagsMaxLength = 500;
		public const int AttributionMaxLength = 200;
		public const int VersionLabelMaxLength = 50;
		public const int ReviewNoteMaxLength = 2000;
		public const int SourceRefMaxLength = 100;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ExchangeListingId { get; set; }

		/// <summary>The contributing department.</summary>
		public int DepartmentId { get; set; }

		/// <summary><see cref="ExchangeItemKinds"/>.</summary>
		public int ItemKind { get; set; }

		/// <summary>The contributed item in the contributor's department (protocol id, training id, …), for their own list only.</summary>
		[MaxLength(SourceRefMaxLength)]
		public string SourceRef { get; set; }

		[MaxLength(TitleMaxLength)]
		public string Title { get; set; }

		[MaxLength(SummaryMaxLength)]
		public string Summary { get; set; }

		[MaxLength(TagsMaxLength)]
		public string Tags { get; set; }

		/// <summary>The contributor's version of the item ("3.1").</summary>
		[MaxLength(VersionLabelMaxLength)]
		public string VersionLabel { get; set; }

		/// <summary>The stripped, self-contained copy (open-format document for a protocol; <see cref="ExchangeTrainingPayload"/> for a training).</summary>
		public string PayloadJson { get; set; }

		/// <summary>SHA-256 (lower-case hex) of <see cref="PayloadJson"/>: what the reviewer passed is what is imported.</summary>
		[MaxLength(64)]
		public string PayloadHash { get; set; }

		/// <summary>An SPDX code from <see cref="ContentLicenses.Contributable"/>.</summary>
		[MaxLength(ContentLicense.MaxLength)]
		public string License { get; set; }

		/// <summary>The name imports credit ("Anytown Fire Department").</summary>
		[MaxLength(AttributionMaxLength)]
		public string AttributionName { get; set; }

		/// <summary>The scrubber's findings for the reviewer (<see cref="ExchangeReviewFlag"/> list as JSON): phone numbers above all.</summary>
		public string ReviewFlagsJson { get; set; }

		/// <summary><see cref="ExchangeListingStatuses"/>.</summary>
		public int Status { get; set; }

		public DateTime SubmittedOn { get; set; }

		public string SubmittedByUserId { get; set; }

		/// <summary>The attestation the contributor agreed to (text version).</summary>
		[MaxLength(20)]
		public string AttestationVersion { get; set; }

		public DateTime? ReviewedOn { get; set; }

		public string ReviewedByUserId { get; set; }

		[MaxLength(ReviewNoteMaxLength)]
		public string ReviewNote { get; set; }

		public DateTime? ListedOn { get; set; }

		public DateTime? WithdrawnOn { get; set; }

		public int RatingCount { get; set; }

		public int RatingTotal { get; set; }

		public int ImportCount { get; set; }

		[NotMapped]
		[JsonIgnore]
		public decimal? AverageRating => RatingCount <= 0 ? (decimal?)null : Math.Round((decimal)RatingTotal / RatingCount, 1, MidpointRounding.AwayFromZero);

		[NotMapped]
		[JsonIgnore]
		public List<ExchangeReviewFlag> ReviewFlags
		{
			get
			{
				if (string.IsNullOrWhiteSpace(ReviewFlagsJson))
					return new List<ExchangeReviewFlag>();
				try
				{
					return JsonConvert.DeserializeObject<List<ExchangeReviewFlag>>(ReviewFlagsJson) ?? new List<ExchangeReviewFlag>();
				}
				catch (JsonException)
				{
					return new List<ExchangeReviewFlag>();
				}
			}
		}

		[NotMapped]
		[JsonIgnore]
		public bool IsListed => Status == (int)ExchangeListingStatuses.Listed;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ExchangeListingId; }
			set { ExchangeListingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ExchangeListings";

		[NotMapped]
		public string IdName => "ExchangeListingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "AverageRating", "ReviewFlags", "IsListed" };
	}

	/// <summary>Column sizes shared by the licence fields.</summary>
	public static class ContentLicense
	{
		public const int MaxLength = 50;
	}

	/// <summary>One member's rating of a listing (1–5), one per member; a department never rates its own listing.</summary>
	[Table("ExchangeRatings")]
	public class ExchangeRating : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ExchangeRatingId { get; set; }

		public int ExchangeListingId { get; set; }

		/// <summary>The rating member's department.</summary>
		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public int Stars { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ExchangeRatingId; }
			set { ExchangeRatingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ExchangeRatings";

		[NotMapped]
		public string IdName => "ExchangeRatingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A comment on a listed item (plain text). Hidden, never deleted, by its author or a reviewer.</summary>
	[Table("ExchangeComments")]
	public class ExchangeComment : IEntity
	{
		public const int BodyMaxLength = 2000;
		public const int AuthorNameMaxLength = 200;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ExchangeCommentId { get; set; }

		public int ExchangeListingId { get; set; }

		/// <summary>The commenting member's department.</summary>
		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary>The department name shown with the comment (other departments never see the member's name).</summary>
		[MaxLength(AuthorNameMaxLength)]
		public string AuthorName { get; set; }

		[MaxLength(BodyMaxLength)]
		public string Body { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? HiddenOn { get; set; }

		public string HiddenByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ExchangeCommentId; }
			set { ExchangeCommentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ExchangeComments";

		[NotMapped]
		public string IdName => "ExchangeCommentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A department's import of a listing: what it created (the copy keeps the listing's attribution).</summary>
	[Table("ExchangeImports")]
	public class ExchangeImport : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ExchangeImportId { get; set; }

		public int ExchangeListingId { get; set; }

		/// <summary>The importing department.</summary>
		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="ExchangeItemKinds"/>.</summary>
		public int ItemKind { get; set; }

		/// <summary>The item created in the importing department.</summary>
		public int CreatedItemId { get; set; }

		public DateTime ImportedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ExchangeImportId; }
			set { ExchangeImportId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ExchangeImports";

		[NotMapped]
		public string IdName => "ExchangeImportId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One finding of the submission scrubber, shown to the contributor before submitting and to the reviewer.</summary>
	public sealed class ExchangeReviewFlag
	{
		/// <summary><see cref="ExchangeReviewFlagKinds"/> name.</summary>
		[JsonProperty("kind")] public string Kind { get; set; }

		/// <summary>What was found (a phone number) or removed (an e-mail address is never kept, only noted as removed).</summary>
		[JsonProperty("text")] public string Text { get; set; }
	}

	public static class ExchangeReviewFlagKinds
	{
		/// <summary>A phone number: left in place for the reviewer to judge (a poison centre number is fine; a station's line is not).</summary>
		public const string PhoneNumber = "PhoneNumber";

		/// <summary>An e-mail address: removed.</summary>
		public const string EmailRemoved = "EmailRemoved";

		/// <summary>The department's own name: replaced with "[Department]".</summary>
		public const string DepartmentNameRemoved = "DepartmentNameRemoved";

		/// <summary>A medical-control contact on a gate block: removed.</summary>
		public const string ContactRemoved = "ContactRemoved";

		/// <summary>A link to the department's own records (another protocol, a POI, an inventory item, a checklist, a training): removed.</summary>
		public const string InternalLinkRemoved = "InternalLinkRemoved";

		/// <summary>An attachment or uploaded image: removed (files are not shared through the exchange).</summary>
		public const string AttachmentRemoved = "AttachmentRemoved";

		/// <summary>A link to the department's inventory on a medication: removed.</summary>
		public const string InventoryLinkRemoved = "InventoryLinkRemoved";

		/// <summary>Triggers, audiences and the number: department configuration, never shared.</summary>
		public const string ConfigurationRemoved = "ConfigurationRemoved";
	}

	/// <summary>Why the exchange refused a step (each maps to a localized message).</summary>
	public enum ExchangeErrors
	{
		None = 0,
		NotAuthorized,
		NotFound,
		/// <summary>Licensed content (MPDS, IntelliComm…) can never be submitted (the P4 guardrail).</summary>
		LicensedContent,
		/// <summary>Only content in force (a published protocol version) can be submitted.</summary>
		NotPublished,
		/// <summary>A document-backed protocol version: its file is the department's and is not shared.</summary>
		DocumentBacked,
		AttestationRequired,
		LicenseRequired,
		/// <summary>The listing is not in a status that allows the step (only listed items can be imported, rated or commented on).</summary>
		WrongStatus,
		/// <summary>A department does not rate its own listing.</summary>
		OwnListing,
		InvalidInput,
		/// <summary>An LMS type needs the department's LMS add-on to import (decision 7c).</summary>
		LmsRequired,
		/// <summary>The item kind has no source on this server.</summary>
		KindUnavailable,
		/// <summary>The payload no longer matches the hash the reviewer passed.</summary>
		PayloadChanged,
		ImportFailed
	}

	/// <summary>Who is acting on the exchange: their department, their rights there, and whether they are a Resgrid reviewer.</summary>
	public sealed class ExchangeActor
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string DepartmentName { get; set; }
		public bool IsCurrentMember { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		/// <summary>Holds Author Protocols (128): may import protocols.</summary>
		public bool CanAuthorProtocols { get; set; }
		/// <summary>Holds Approve Protocols (129): may submit protocols (sharing outside the department is a publish).</summary>
		public bool CanApproveProtocols { get; set; }
		/// <summary>May create trainings (the Training create right).</summary>
		public bool CanCreateTrainings { get; set; }
		/// <summary>A Resgrid system operator (the review queue).</summary>
		public bool IsReviewer { get; set; }
	}

	/// <summary>An item a department could submit (the picker).</summary>
	public sealed class ExchangeSourceItem
	{
		public int ItemKind { get; set; }
		public string SourceRef { get; set; }
		public string Title { get; set; }
		public string VersionLabel { get; set; }
		/// <summary>Why it cannot be submitted (licensed, not published…), or None.</summary>
		public ExchangeErrors Blocked { get; set; }
	}

	/// <summary>A submission as the scrubber left it: the stripped payload, its findings and the listing fields.</summary>
	public sealed class ExchangePreparedItem
	{
		public ExchangeErrors Error { get; set; }
		public int ItemKind { get; set; }
		public string SourceRef { get; set; }
		public string Title { get; set; }
		public string Summary { get; set; }
		public string Tags { get; set; }
		public string VersionLabel { get; set; }
		public string PayloadJson { get; set; }
		public List<ExchangeReviewFlag> Flags { get; set; } = new List<ExchangeReviewFlag>();
		/// <summary>Sanitized display HTML of the payload: exactly what the listing will show and an import will create.</summary>
		public string PreviewHtml { get; set; }
	}

	public sealed class ExchangeSubmitRequest
	{
		public int ItemKind { get; set; }
		public string SourceRef { get; set; }
		public string Summary { get; set; }
		public string License { get; set; }
		public string AttributionName { get; set; }
		/// <summary>The contributor confirmed the attestation (<see cref="ExchangeRules.AttestationVersion"/>).</summary>
		public bool Attested { get; set; }
	}

	public sealed class ExchangeResult
	{
		public ExchangeErrors Error { get; set; }
		public ExchangeListing Listing { get; set; }
		public ExchangePreparedItem Prepared { get; set; }
		/// <summary>For an import: what was created in the department (protocol id, training id, …).</summary>
		public int? CreatedItemId { get; set; }
		public bool Succeeded => Error == ExchangeErrors.None;

		public static ExchangeResult Fail(ExchangeErrors error) => new ExchangeResult { Error = error };
	}

	/// <summary>The browse page's query.</summary>
	public sealed class ExchangeBrowseQuery
	{
		public int? ItemKind { get; set; }
		public string Search { get; set; }
		/// <summary>"rating", "imports" or "newest" (default).</summary>
		public string Sort { get; set; }
		public int Skip { get; set; }
		public int Take { get; set; } = 25;
	}

	/// <summary>A listing with what the reader sees around it.</summary>
	public sealed class ExchangeListingView
	{
		public ExchangeListing Listing { get; set; }
		/// <summary>Sanitized display HTML of the payload (a preview of what an import creates).</summary>
		public string PreviewHtml { get; set; }
		public List<ExchangeComment> Comments { get; set; } = new List<ExchangeComment>();
		public int? MyStars { get; set; }
		public bool IsOwnDepartment { get; set; }
		public bool CanImport { get; set; }
		public bool ImportNeedsLms { get; set; }
		public bool AlreadyImported { get; set; }
	}

	/// <summary>A baseline Training as the exchange carries it (no assignees, dates, audiences, CE provider data or attachments).</summary>
	public sealed class ExchangeTrainingPayload
	{
		public const string FormatName = "resgrid-training";
		public const int CurrentVersion = 1;

		[JsonProperty("format")] public string Format { get; set; } = FormatName;
		[JsonProperty("version")] public int Version { get; set; } = CurrentVersion;
		[JsonProperty("name")] public string Name { get; set; }
		[JsonProperty("description")] public string Description { get; set; }
		/// <summary>Sanitized rich text.</summary>
		[JsonProperty("text")] public string TrainingText { get; set; }
		[JsonProperty("minimumScore")] public double MinimumScore { get; set; }
		[JsonProperty("hours")] public decimal? Hours { get; set; }
		[JsonProperty("objectives")] public string Objectives { get; set; }
		[JsonProperty("tags")] public string Tags { get; set; }
		[JsonProperty("questions")] public List<ExchangeTrainingQuestion> Questions { get; set; } = new List<ExchangeTrainingQuestion>();
	}

	public sealed class ExchangeTrainingQuestion
	{
		[JsonProperty("question")] public string Question { get; set; }
		[JsonProperty("type")] public int Type { get; set; }
		[JsonProperty("answers")] public List<ExchangeTrainingAnswer> Answers { get; set; } = new List<ExchangeTrainingAnswer>();
	}

	public sealed class ExchangeTrainingAnswer
	{
		[JsonProperty("answer")] public string Answer { get; set; }
		[JsonProperty("correct")] public bool Correct { get; set; }
	}

	/// <summary>The exchange's fixed rules: the attestation, rating bounds and the text scrubber.</summary>
	public static class ExchangeRules
	{
		/// <summary>The attestation text version the contributor agrees to (the text lives in the localized resources).</summary>
		public const string AttestationVersion = "exchange-attest/1";

		public const int MinStars = 1;
		public const int MaxStars = 5;
		public const int MaxPayloadLength = 2_000_000;

		/// <summary>The listing payload's hash (SHA-256, lower-case hex), as protocol content hashes are written.</summary>
		public static string Hash(string payload) => ProtocolContentHash.Compute(payload ?? string.Empty);

		public static bool IsLmsKind(int kind) => kind == (int)ExchangeItemKinds.SkillSheet || kind == (int)ExchangeItemKinds.LessonPlan;
	}

	/// <summary>
	/// Strips department-specific data from text before it leaves the department (P5): e-mail addresses are removed, the department's own
	/// name becomes "[Department]", and phone numbers are kept but flagged for the reviewer (a public number such as a poison centre is
	/// fine; a station's direct line is not). Works on plain text and on sanitized HTML (text nodes only; markup is never rewritten).
	/// </summary>
	public sealed class ExchangeScrubber
	{
		public const string DepartmentPlaceholder = "[Department]";
		public const string EmailPlaceholder = "[e-mail removed]";

		private static readonly Regex Email = new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		/// <summary>
		/// A phone number: an optional +country code, then 7 to 15 digits in groups separated by spaces, dots, dashes or brackets. Dates,
		/// version numbers and doses ("2.5 mg") are too short or shaped differently to match.
		/// </summary>
		private static readonly Regex Phone = new Regex(@"(?<!\w)(?<!\d\.)(\+\d{1,3}[\s.-]?)?(\(\d{1,4}\)[\s.-]?)?\d{2,4}([\s.-]\d{2,4}){1,4}(?!\w)(?!\.\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		/// <summary>A date written with separators (2026-11-25, 25.11.2026, 11/25/26): never a phone number.</summary>
		private static readonly Regex DateShape = new Regex(@"^(\d{4}[-./]\d{1,2}[-./]\d{1,2}|\d{1,2}[-./]\d{1,2}[-./]\d{2,4})$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		private static readonly Regex Tag = new Regex(@"(<[^>]*>)", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		private readonly Regex _departmentName;

		public List<ExchangeReviewFlag> Flags { get; } = new List<ExchangeReviewFlag>();

		public ExchangeScrubber(string departmentName)
		{
			var name = departmentName?.Trim();
			_departmentName = string.IsNullOrEmpty(name) || name.Length < 3
				? null
				: new Regex(@"(?<!\w)" + Regex.Escape(name) + @"(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
		}

		public void Flag(string kind, string text = null)
		{
			if (Flags.Any(f => f.Kind == kind && string.Equals(f.Text, text, StringComparison.Ordinal)))
				return;
			Flags.Add(new ExchangeReviewFlag { Kind = kind, Text = text });
		}

		/// <summary>Plain text, scrubbed.</summary>
		public string Text(string value)
		{
			if (string.IsNullOrEmpty(value))
				return value;
			var text = Email.Replace(value, m =>
			{
				Flag(ExchangeReviewFlagKinds.EmailRemoved);
				return EmailPlaceholder;
			});
			if (_departmentName != null)
			{
				text = _departmentName.Replace(text, m =>
				{
					Flag(ExchangeReviewFlagKinds.DepartmentNameRemoved);
					return DepartmentPlaceholder;
				});
			}
			foreach (Match match in Phone.Matches(text))
			{
				var digits = match.Value.Count(char.IsDigit);
				if (digits >= 7 && digits <= 15 && !DateShape.IsMatch(match.Value.Trim()))
					Flag(ExchangeReviewFlagKinds.PhoneNumber, match.Value.Trim());
			}
			return text;
		}

		/// <summary>HTML, scrubbed in its text nodes only (tags and attributes are left to the sanitizer). mailto: links lose their address.</summary>
		public string Html(string html)
		{
			if (string.IsNullOrEmpty(html))
				return html;
			var parts = Tag.Split(html);
			for (var i = 0; i < parts.Length; i++)
			{
				if (parts[i].StartsWith("<", StringComparison.Ordinal))
				{
					if (parts[i].IndexOf("mailto:", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						Flag(ExchangeReviewFlagKinds.EmailRemoved);
						parts[i] = Regex.Replace(parts[i], @"mailto:[^""'\s>]*", "#", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
					}
					continue;
				}
				// Text between tags is HTML-encoded; decode so names and addresses written with entities still match, then encode back.
				var decoded = WebUtility.HtmlDecode(parts[i]);
				var scrubbed = Text(decoded);
				if (!string.Equals(decoded, scrubbed, StringComparison.Ordinal))
					parts[i] = WebUtility.HtmlEncode(scrubbed);
			}
			return string.Concat(parts);
		}

		/// <summary>
		/// A protocol block document with the department's references taken out: internal links, attachments and uploaded images,
		/// inventory links on medications, gate contacts and scope tags; every text scrubbed. The decision graph keeps its shape but
		/// loses its priority ids (priority names travel). Returns a normalized copy.
		/// </summary>
		public ProtocolContentDocument Document(ProtocolContentDocument document)
		{
			if (document == null)
				return null;

			var copy = JsonConvert.DeserializeObject<ProtocolContentDocument>(JsonConvert.SerializeObject(document)) ?? new ProtocolContentDocument();
			copy.Blocks = Blocks(copy.Blocks);
			if (copy.Graph != null)
			{
				foreach (var node in copy.Graph.Nodes ?? new List<ProtocolDecisionNode>())
				{
					if (node == null)
						continue;
					node.PriorityId = null;
					node.Label = Text(node.Label);
					node.Text = Text(node.Text);
					node.ResponseHint = Text(node.ResponseHint);
					node.PostDispatch = Text(node.PostDispatch);
					foreach (var answer in node.Answers ?? new List<ProtocolDecisionAnswer>())
						if (answer != null)
							answer.Label = Text(answer.Label);
				}
			}

			if (copy.IsBlockModel)
				return ProtocolBlocks.Normalize(copy);

			// A P1 html document: its blocks scrubbed, anchors recomputed.
			copy.Anchors = ProtocolContentDocument.Outline(string.Concat((copy.Blocks ?? new List<ProtocolContentBlock>()).Select(b => b.Html ?? string.Empty)), "b1");
			return copy;
		}

		private List<ProtocolContentBlock> Blocks(IEnumerable<ProtocolContentBlock> blocks)
		{
			var list = new List<ProtocolContentBlock>();
			foreach (var block in blocks ?? Enumerable.Empty<ProtocolContentBlock>())
			{
				if (block == null)
					continue;
				block.Scopes = null;
				switch (block.Type)
				{
					case ProtocolBlockTypes.Attachment:
						Flag(ExchangeReviewFlagKinds.AttachmentRemoved);
						continue;
					case ProtocolBlockTypes.Image when block.AttachmentId.HasValue || ContentLicenses.SafeUrl(block.Url) == null || !block.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase):
						Flag(ExchangeReviewFlagKinds.AttachmentRemoved);
						continue;
					case ProtocolBlockTypes.Link when !string.Equals(block.LinkKind, ProtocolLinkKinds.Url, StringComparison.Ordinal):
						Flag(ExchangeReviewFlagKinds.InternalLinkRemoved);
						continue;
				}

				block.Html = Html(block.Html);
				block.Text = Text(block.Text);
				block.Title = Text(block.Title);
				block.Label = Text(block.Label);
				block.Alt = Text(block.Alt);
				if (!string.IsNullOrWhiteSpace(block.Contact))
				{
					Flag(ExchangeReviewFlagKinds.ContactRemoved);
					block.Contact = null;
				}
				if (block.Items != null)
					foreach (var item in block.Items.Where(i => i != null))
						item.Html = Html(item.Html);
				if (block.Rows != null)
					block.Rows = block.Rows.Select(r => r?.Select(Text).ToList()).ToList();
				if (block.Medication != null)
				{
					if (!string.IsNullOrWhiteSpace(block.Medication.InventoryItemId))
						Flag(ExchangeReviewFlagKinds.InventoryLinkRemoved);
					block.Medication.InventoryItemId = null;
					block.Medication.InventoryItemName = null;
					block.Medication.Notes = Html(block.Medication.Notes);
				}
				if (block.Variants != null)
					foreach (var variant in block.Variants.Where(v => v != null))
					{
						variant.Label = Text(variant.Label);
						variant.Blocks = Blocks(variant.Blocks);
					}
				list.Add(block);
			}
			return list;
		}
	}
}
