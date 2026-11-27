using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model.Lms
{
	// Training T5 item 3 (round 6C): content standards in the LMS add-on (§17.2 "Standards"). SCORM 1.2 / 2004 and cmi5 packages are
	// imported into the LMS media store and played in a sandboxed iframe; xAPI statements go out to a department LRS; LTI 1.3 tools
	// (third-party CE providers) launch from a lesson and report a score back. LMS tables reference baseline ids only (§17.3).

	/// <summary>The e-learning standard a content package follows (LmsContentPackages.Standard). Persisted: append only.</summary>
	public enum LmsContentStandards
	{
		Scorm12 = 0,

		/// <summary>SCORM 2004 (3rd or 4th edition): one runtime, the API_1484_11 adapter.</summary>
		Scorm2004 = 1,

		Cmi5 = 2
	}

	/// <summary>A content package's state (LmsContentPackages.Status, worker 98). Persisted: append only.</summary>
	public enum LmsContentPackageStatuses
	{
		/// <summary>Validated and stored; waiting for worker 98 to unpack it.</summary>
		Queued = 0,

		/// <summary>Claimed by worker 98 (ClaimedOn says when; a stale claim goes back to the queue).</summary>
		Processing = 1,

		/// <summary>Unpacked under its content prefix: lessons may use it and learners may launch it.</summary>
		Ready = 2,

		/// <summary>Unpacking failed MaxAttempts times; the author uploads again.</summary>
		Failed = 3
	}

	/// <summary>Which runtime a registration belongs to (LmsRuntimeRegistrations.Kind, LmsRuntimeSessions.Kind). Persisted: append only.</summary>
	public enum LmsRuntimeKinds
	{
		Scorm12 = 0,
		Scorm2004 = 1,
		Cmi5 = 2,

		/// <summary>An LTI 1.3 resource link launch (the score comes back through Assignment and Grade Services).</summary>
		Lti = 3,

		/// <summary>
		/// Round 10 A8 (sessions only): the single-use launch link a bearer client (Omni) opens in its web view. TokenHash is the link's token,
		/// UsedOn when the launch page exchanged it, ExpiresOn at most five minutes out. Never a content, LRS or LTI credential.
		/// </summary>
		AppLaunch = 4,

		/// <summary>
		/// Round 10 A8 (sessions only): the in-app launch page's own session for one member, enrollment and lesson (TokenHash, the launch hours).
		/// It reads that lesson's state and relaunches its items or its LTI tool, nothing else; the content and LTI sessions it mints carry its id
		/// in <see cref="LmsRuntimeSession.AppSessionId"/>.
		/// </summary>
		AppSession = 5
	}

	/// <summary>A department's LTI platform signing key (LmsLtiPlatformKeys.Status). Persisted: append only.</summary>
	public enum LmsLtiKeyStatuses
	{
		/// <summary>Signs new launches; published in the key set.</summary>
		Active = 0,

		/// <summary>Rotated out: no longer signs, still published for <see cref="LmsLtiRules.RetiredKeyPublishedFor"/> so launches in flight verify.</summary>
		Retired = 1
	}

	/// <summary>An outgoing xAPI statement's state (LmsXapiOutbox.Status, worker 98). Persisted: append only.</summary>
	public enum LmsXapiOutboxStatuses
	{
		Pending = 0,

		/// <summary>Claimed by worker 98 (ClaimedOn says when; a stale claim is retried).</summary>
		Sending = 1,

		Delivered = 2,

		/// <summary>Refused for good (a 4xx the LRS will keep refusing) or out of attempts; kept for the officer to see.</summary>
		Failed = 3
	}

	/// <summary>Why a content package was refused at upload (each maps to a localized message on the web).</summary>
	public enum LmsPackageErrors
	{
		None = 0,
		Empty,
		TooLarge,
		NotZip,
		TooManyFiles,

		/// <summary>An entry whose path leaves the package folder (zip-slip: "..", an absolute or drive path, a backslash or a control character).</summary>
		UnsafePath,

		/// <summary>The files would unpack to more than the cap.</summary>
		UncompressedTooLarge,

		/// <summary>An entry compresses far beyond what real content does (a zip bomb).</summary>
		SuspiciousCompression,

		/// <summary>No imsmanifest.xml (SCORM) or cmi5.xml (cmi5) at the root of the package.</summary>
		ManifestMissing,

		/// <summary>The manifest is not well-formed XML or misses what the standard requires.</summary>
		ManifestInvalid,

		/// <summary>A SCORM version this player does not run (e.g. SCORM 2004 2nd edition with sequencing only, AICC).</summary>
		UnsupportedStandard,

		/// <summary>No launchable SCO or AU.</summary>
		NoLaunchableItem,

		/// <summary>A SCO or AU names a file that is not in the package.</summary>
		LaunchFileMissing,

		/// <summary>A cmi5 AU hosted elsewhere (an absolute URL): only packaged AUs run inside Resgrid's sandbox.</summary>
		RemoteAuNotSupported
	}

	/// <summary>
	/// An imported SCORM or cmi5 package (T5 item 3): the uploaded zip (<see cref="SourceKey"/>) and, once worker 98 unpacked it, its files
	/// under <see cref="ContentPrefix"/> in the LMS media store. The parsed manifest (<see cref="ManifestJson"/>) lists the SCOs or AUs in
	/// order. A lesson of kind Scorm or Cmi5 points at one package; deleting a package hides it from new lessons only.
	/// </summary>
	[Table("LmsContentPackages")]
	public class LmsContentPackage : IEntity
	{
		public const int TitleMaxLength = 250;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsContentPackageId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>
		/// Set when this row shares another department's stored package (a course set copy, round 10 A3, M0418): the department whose keys
		/// SourceKey/ContentPrefix name. A store purge must not delete objects while any row names them. Null for the department's own uploads.
		/// </summary>
		public int? SharedFromDepartmentId { get; set; }

		/// <summary><see cref="LmsContentStandards"/>.</summary>
		public int Standard { get; set; }

		[MaxLength(TitleMaxLength)]
		public string Title { get; set; }

		/// <summary>The manifest's identifier (SCORM manifest identifier, cmi5 course id).</summary>
		public string Identifier { get; set; }

		/// <summary>The schema version the manifest declared ("1.2", "2004 4th Edition", "cmi5").</summary>
		public string SchemaVersion { get; set; }

		public string FileName { get; set; }

		/// <summary>SHA-256 (lower-case hex) of the uploaded zip.</summary>
		public string Sha256 { get; set; }

		/// <summary><see cref="LmsContentPackageStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>The uploaded zip's object key.</summary>
		public string SourceKey { get; set; }

		public long SourceBytes { get; set; }

		/// <summary>The key prefix the unpacked files live under ("…/content/").</summary>
		public string ContentPrefix { get; set; }

		public int FileCount { get; set; }

		/// <summary>The unpacked size (counts toward the department's included LMS storage, reported only).</summary>
		public long ContentBytes { get; set; }

		/// <summary>The parsed manifest (<see cref="LmsPackageManifest"/>).</summary>
		public string ManifestJson { get; set; }

		public int Attempts { get; set; }

		public string LastError { get; set; }

		public DateTime? ClaimedOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? ReadyOn { get; set; }

		/// <summary>Hidden from new lessons (lessons that use it keep playing).</summary>
		public bool IsDeleted { get; set; }

		public DateTime? DeletedOn { get; set; }

		public string DeletedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsContentPackageId; }
			set { LmsContentPackageId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsContentPackages";

		[NotMapped]
		public string IdName => "LmsContentPackageId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One learner's runtime record for one standards lesson of one enrollment (unique per enrollment and lesson): the SCORM data model per
	/// SCO (suspend data, location, score, completion and success), the cmi5 registration and its results, or the LTI score passed back.
	/// <see cref="Satisfied"/> is what completes the lesson's <see cref="LmsLessonProgress"/>.
	/// </summary>
	[Table("LmsRuntimeRegistrations")]
	public class LmsRuntimeRegistration : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsRuntimeRegistrationId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsEnrollmentId { get; set; }

		public int LmsLessonId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="LmsRuntimeKinds"/>.</summary>
		public int Kind { get; set; }

		public int? LmsContentPackageId { get; set; }

		public int? LmsLtiToolId { get; set; }

		/// <summary>cmi5: the registration (a GUID). LTI: the pseudonymous subject the tool knows the learner by.</summary>
		public string RegistrationKey { get; set; }

		/// <summary>SCORM: <see cref="LmsScormState"/>; cmi5: <see cref="LmsCmi5State"/>; LTI: <see cref="LmsLtiScoreState"/>.</summary>
		public string StateJson { get; set; }

		/// <summary>The summary completion status (SCORM vocabulary, "completed" for cmi5/LTI).</summary>
		public string CompletionStatus { get; set; }

		/// <summary>"passed", "failed" or "unknown".</summary>
		public string SuccessStatus { get; set; }

		/// <summary>The score as a percentage (0–100) when the content reported one.</summary>
		public decimal? ScorePercent { get; set; }

		/// <summary>Seconds in the content, from the sessions' reported time (SCORM) or wall-clock (cmi5).</summary>
		public int TotalSeconds { get; set; }

		public int LaunchCount { get; set; }

		/// <summary>The lesson's completion rule is met (SCORM status, cmi5 moveOn, LTI FullyGraded at or above the pass mark).</summary>
		public bool Satisfied { get; set; }

		public DateTime? SatisfiedOn { get; set; }

		/// <summary>LTI: the timestamp of the last score applied (an older one is ignored, AGS).</summary>
		public DateTime? LastResultOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsRuntimeRegistrationId; }
			set { LmsRuntimeRegistrationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsRuntimeRegistrations";

		[NotMapped]
		public string IdName => "LmsRuntimeRegistrationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One launch: the bearer token the sandboxed content uses (only its SHA-256 is stored), and for cmi5 the one-time fetch token and the
	/// auth token the AU sends to the minimal LRS. LTI launches use <see cref="TokenHash"/> for the lti_message_hint, single use. Round 10 A8:
	/// an in-app launch link (<see cref="LmsRuntimeKinds.AppLaunch"/>, single use) and the launch page's session
	/// (<see cref="LmsRuntimeKinds.AppSession"/>) are rows here too; every reader checks the kind it expects.
	/// </summary>
	[Table("LmsRuntimeSessions")]
	public class LmsRuntimeSession : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsRuntimeSessionId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsRuntimeRegistrationId { get; set; }

		public int LmsEnrollmentId { get; set; }

		public int LmsLessonId { get; set; }

		public string UserId { get; set; }

		/// <summary><see cref="LmsRuntimeKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>The SCO or AU launched (its manifest identifier).</summary>
		public string ItemIdentifier { get; set; }

		/// <summary>cmi5: the session id (a GUID) every AU statement carries in its context.</summary>
		public string SessionKey { get; set; }

		/// <summary>SHA-256 of the content token (SCORM/cmi5) or of the lti_message_hint (LTI).</summary>
		public string TokenHash { get; set; }

		/// <summary>cmi5: SHA-256 of the one-time fetch token.</summary>
		public string FetchTokenHash { get; set; }

		/// <summary>cmi5: SHA-256 of the auth token the fetch URL handed out.</summary>
		public string AuthTokenHash { get; set; }

		public DateTime? FetchedOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ExpiresOn { get; set; }

		/// <summary>LTI: when the login request consumed the hint (single use).</summary>
		public DateTime? UsedOn { get; set; }

		/// <summary>SCORM: finished; cmi5: terminated. No further writes are accepted.</summary>
		public DateTime? EndedOn { get; set; }

		/// <summary>
		/// Round 10 A8 (M0421): the in-app launch session (<see cref="LmsRuntimeKinds.AppSession"/>) that minted this content session or LTI hint;
		/// null for a web launch. It lets the app's web build frame the content and gives an LTI tool a return URL.
		/// </summary>
		public int? AppSessionId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsRuntimeSessionId; }
			set { LmsRuntimeSessionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsRuntimeSessions";

		[NotMapped]
		public string IdName => "LmsRuntimeSessionId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A statement an AU sent to the minimal LRS (cmi5 keeps full results, §13a). Unique per registration and statement id.</summary>
	[Table("LmsCmi5Statements")]
	public class LmsCmi5Statement : IEntity
	{
		public const int MaxStatementBytes = 64 * 1024;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCmi5StatementId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsRuntimeRegistrationId { get; set; }

		public int LmsRuntimeSessionId { get; set; }

		public string StatementId { get; set; }

		public string VerbId { get; set; }

		public string StatementJson { get; set; }

		public DateTime StoredOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCmi5StatementId; }
			set { LmsCmi5StatementId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCmi5Statements";

		[NotMapped]
		public string IdName => "LmsCmi5StatementId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An xAPI State API document of a cmi5 registration (LMS.LaunchData is generated, the AU's own bookmarks are stored here).</summary>
	[Table("LmsCmi5StateDocuments")]
	public class LmsCmi5StateDocument : IEntity
	{
		public const int MaxContentBytes = 64 * 1024;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCmi5StateDocumentId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsRuntimeRegistrationId { get; set; }

		public string ActivityId { get; set; }

		public string StateId { get; set; }

		public string ContentType { get; set; }

		public string Content { get; set; }

		public DateTime UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCmi5StateDocumentId; }
			set { LmsCmi5StateDocumentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCmi5StateDocuments";

		[NotMapped]
		public string IdName => "LmsCmi5StateDocumentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// An LTI 1.3 tool the department registered (platform side, T5 item 3): Resgrid is the platform and issues the client id; the tool gives
	/// its OIDC login URL, launch (target link) URL, redirect URIs and its key set (a JWKS URL, or the key set pasted in). Lessons of kind
	/// LtiLink launch it; Assignment and Grade Services pass the score back when <see cref="AllowGradePassback"/>.
	/// </summary>
	[Table("LmsLtiTools")]
	public class LmsLtiTool : IEntity
	{
		public const int NameMaxLength = 200;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsLtiToolId { get; set; }

		public int DepartmentId { get; set; }

		[MaxLength(NameMaxLength)]
		public string Name { get; set; }

		/// <summary>The client id Resgrid issued the tool (a GUID, unique).</summary>
		public string ClientId { get; set; }

		/// <summary>The deployment id Resgrid issued the tool.</summary>
		public string DeploymentId { get; set; }

		/// <summary>The tool's third-party-initiated login URL (https).</summary>
		public string OidcLoginUrl { get; set; }

		/// <summary>The tool's default target link URI (https).</summary>
		public string LaunchUrl { get; set; }

		/// <summary>Redirect URIs the id_token may be posted to, one per line (the launch URL when empty).</summary>
		public string RedirectUris { get; set; }

		/// <summary>The tool's public key set URL (https), fetched to verify its client assertions.</summary>
		public string JwksUrl { get; set; }

		/// <summary>The tool's public key set pasted in (a JWKS document), used instead of <see cref="JwksUrl"/> when present.</summary>
		public string PublicJwksJson { get; set; }

		/// <summary>Custom parameters sent with every launch, "key=value" per line.</summary>
		public string CustomParameters { get; set; }

		/// <summary>The tool may post scores back (AGS); completion then follows the score.</summary>
		public bool AllowGradePassback { get; set; }

		/// <summary>Launches carry the learner's name (off: only the pseudonymous subject).</summary>
		public bool ShareName { get; set; }

		/// <summary>Launches carry the learner's e-mail address (off by default).</summary>
		public bool ShareEmail { get; set; }

		public bool IsActive { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsLtiToolId; }
			set { LmsLtiToolId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsLtiTools";

		[NotMapped]
		public string IdName => "LmsLtiToolId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// The department's LTI platform signing key (RS256): the public half as a JWK, published at the department's key set URL; the private
	/// half encrypted with the department key (IEncryptionService.EncryptForDepartment). Rotating adds a new active key and retires the old.
	/// </summary>
	[Table("LmsLtiPlatformKeys")]
	public class LmsLtiPlatformKey : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsLtiPlatformKeyId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The key id (kid) in the JWT header and the key set.</summary>
		public string Kid { get; set; }

		public string PublicJwkJson { get; set; }

		/// <summary>The PKCS#8 private key (base64), encrypted for the department. Never leaves the service.</summary>
		public string PrivateKeyEncrypted { get; set; }

		/// <summary><see cref="LmsLtiKeyStatuses"/>.</summary>
		public int Status { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? RetiredOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsLtiPlatformKeyId; }
			set { LmsLtiPlatformKeyId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsLtiPlatformKeys";

		[NotMapped]
		public string IdName => "LmsLtiPlatformKeyId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An AGS access token Resgrid issued a tool (client credentials with a signed assertion). Only its SHA-256 is stored.</summary>
	[Table("LmsLtiAccessTokens")]
	public class LmsLtiAccessToken : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsLtiAccessTokenId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsLtiToolId { get; set; }

		public string TokenHash { get; set; }

		/// <summary>The granted scopes, space separated.</summary>
		public string Scopes { get; set; }

		/// <summary>The client assertion's jti: unique per tool, so an assertion cannot be replayed.</summary>
		public string AssertionJti { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ExpiresOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsLtiAccessTokenId; }
			set { LmsLtiAccessTokenId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsLtiAccessTokens";

		[NotMapped]
		public string IdName => "LmsLtiAccessTokenId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// An xAPI statement waiting for (or delivered to) the department's external LRS (T5 item 3). Built when the learning event happens, with
	/// the department-scoped account as the actor (an e-mail address only when the department opted in), delivered by worker 98 with retry
	/// and backoff.
	/// </summary>
	[Table("LmsXapiOutbox")]
	public class LmsXapiOutboxItem : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsXapiOutboxItemId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The statement id (a GUID): an LRS answering 409 for it already has it.</summary>
		public string StatementId { get; set; }

		/// <summary>The member the statement is about (for erasure); never sent unless it is the opted-in e-mail actor.</summary>
		public string UserId { get; set; }

		public string VerbId { get; set; }

		public string StatementJson { get; set; }

		/// <summary><see cref="LmsXapiOutboxStatuses"/>.</summary>
		public int Status { get; set; }

		public int Attempts { get; set; }

		public DateTime NextAttemptOn { get; set; }

		public DateTime? ClaimedOn { get; set; }

		public string LastError { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? DeliveredOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsXapiOutboxItemId; }
			set { LmsXapiOutboxItemId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsXapiOutbox";

		[NotMapped]
		public string IdName => "LmsXapiOutboxItemId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// ProtoBuf <see cref="DepartmentSettingTypes.LmsStandardsSettings"/> (147), Trainings → Standards → xAPI (LMS, T5): where xAPI statements
	/// go. Missing = off. The LRS secret is stored encrypted for the department and never shown again. Every member defaults to false/empty,
	/// so nothing needs a [DefaultValue] (protobuf-true-default-drop).
	/// </summary>
	[ProtoContract]
	public class LmsStandardsSettings
	{
		/// <summary>Send xAPI statements (completed, passed, failed, attempted) to the LRS below.</summary>
		[ProtoMember(1)]
		public bool XapiEnabled { get; set; }

		/// <summary>The LRS endpoint (https), e.g. https://lrs.example.org/xapi/.</summary>
		[ProtoMember(2)]
		public string LrsEndpoint { get; set; }

		/// <summary>The LRS basic-auth key (user name).</summary>
		[ProtoMember(3)]
		public string LrsKey { get; set; }

		/// <summary>The LRS basic-auth secret, encrypted for the department (IEncryptionService.EncryptForDepartment).</summary>
		[ProtoMember(4)]
		public string LrsSecretEncrypted { get; set; }

		/// <summary>The actor is the member's e-mail address (mbox) instead of the department-scoped account. Off by default.</summary>
		[ProtoMember(5)]
		public bool ShareEmailInActor { get; set; }

		[JsonIgnore]
		public bool IsDeliverable => XapiEnabled && LmsStandardsUrls.IsAcceptableHttpsUrl(LrsEndpoint);
	}

	/// <summary>
	/// A package's parsed manifest: the standard, the title and the launchable items in order (SCORM SCOs, cmi5 AUs). Stored as JSON on the
	/// package so a launch never reads the zip again.
	/// </summary>
	public sealed class LmsPackageManifest
	{
		/// <summary><see cref="LmsContentStandards"/>.</summary>
		public int Standard { get; set; }

		public string Title { get; set; }

		public string Identifier { get; set; }

		public string SchemaVersion { get; set; }

		public List<LmsPackageItem> Items { get; set; } = new List<LmsPackageItem>();

		public string ToJson() => JsonConvert.SerializeObject(this);

		public static LmsPackageManifest Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new LmsPackageManifest();
			try
			{
				return JsonConvert.DeserializeObject<LmsPackageManifest>(json) ?? new LmsPackageManifest();
			}
			catch (JsonException)
			{
				return new LmsPackageManifest();
			}
		}
	}

	/// <summary>One launchable item of a package: a SCORM SCO or a cmi5 AU.</summary>
	public sealed class LmsPackageItem
	{
		/// <summary>The SCO's item identifier, or the AU's id (an IRI).</summary>
		public string Identifier { get; set; }

		public string Title { get; set; }

		/// <summary>The launch file, relative to the package root, with any parameters (SCORM href + parameters, the AU url).</summary>
		public string Href { get; set; }

		/// <summary>SCORM adlcp:dataFromLMS / cmi5 launchParameters: handed to the content at launch.</summary>
		public string LaunchData { get; set; }

		/// <summary>SCORM 1.2 adlcp:masteryscore (0–100); SCORM 2004 minNormalizedMeasure and cmi5 masteryScore (0–1).</summary>
		public decimal? MasteryScore { get; set; }

		/// <summary>SCORM 2004 adlcp:completionThreshold (0–1).</summary>
		public decimal? CompletionThreshold { get; set; }

		/// <summary>cmi5 moveOn: Passed, Completed, CompletedAndPassed, CompletedOrPassed or NotApplicable.</summary>
		public string MoveOn { get; set; }

		/// <summary>cmi5 launchMethod: AnyWindow or OwnWindow.</summary>
		public string LaunchMethod { get; set; }
	}

	/// <summary>What the inspection of an uploaded zip found (validation before anything is stored).</summary>
	public sealed class LmsPackageInspection
	{
		public LmsPackageErrors Error { get; set; }

		/// <summary>The entry or element the error is about (shown to the author).</summary>
		public string Detail { get; set; }

		public LmsPackageManifest Manifest { get; set; }

		public int FileCount { get; set; }

		public long UncompressedBytes { get; set; }

		public bool Succeeded => Error == LmsPackageErrors.None && Manifest != null;

		public static LmsPackageInspection Fail(LmsPackageErrors error, string detail = null) => new LmsPackageInspection { Error = error, Detail = detail };
	}

	/// <summary>The SCORM data model of a registration: one state per SCO (multi-SCO packages complete when every SCO is satisfied).</summary>
	public sealed class LmsScormState
	{
		public Dictionary<string, LmsScoState> Scos { get; set; } = new Dictionary<string, LmsScoState>(StringComparer.Ordinal);

		public string ToJson() => JsonConvert.SerializeObject(this);

		public static LmsScormState Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new LmsScormState();
			try
			{
				var state = JsonConvert.DeserializeObject<LmsScormState>(json) ?? new LmsScormState();
				state.Scos = new Dictionary<string, LmsScoState>(state.Scos ?? new Dictionary<string, LmsScoState>(), StringComparer.Ordinal);
				return state;
			}
			catch (JsonException)
			{
				return new LmsScormState();
			}
		}
	}

	/// <summary>One SCO's stored data model and what the server derived from it.</summary>
	public sealed class LmsScoState
	{
		/// <summary>Every accepted writable element (cmi.* key to value).</summary>
		public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

		public string Completion { get; set; }

		public string Success { get; set; }

		public decimal? ScorePercent { get; set; }

		public int TotalSeconds { get; set; }

		public bool Satisfied { get; set; }

		/// <summary>The last session ended with exit "suspend" (the next launch is a resume).</summary>
		public bool Suspended { get; set; }
	}

	/// <summary>The results of a cmi5 registration (each AU's completed / passed / failed and its best score).</summary>
	public sealed class LmsCmi5State
	{
		public Dictionary<string, LmsCmi5AuState> Aus { get; set; } = new Dictionary<string, LmsCmi5AuState>(StringComparer.Ordinal);

		public string ToJson() => JsonConvert.SerializeObject(this);

		public static LmsCmi5State Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new LmsCmi5State();
			try
			{
				var state = JsonConvert.DeserializeObject<LmsCmi5State>(json) ?? new LmsCmi5State();
				state.Aus = new Dictionary<string, LmsCmi5AuState>(state.Aus ?? new Dictionary<string, LmsCmi5AuState>(), StringComparer.Ordinal);
				return state;
			}
			catch (JsonException)
			{
				return new LmsCmi5State();
			}
		}
	}

	public sealed class LmsCmi5AuState
	{
		public bool Completed { get; set; }
		public bool Passed { get; set; }
		public bool Failed { get; set; }
		public decimal? ScoreScaled { get; set; }
		public bool Satisfied { get; set; }
	}

	/// <summary>The last score an LTI tool passed back (AGS).</summary>
	public sealed class LmsLtiScoreState
	{
		public decimal? ScoreGiven { get; set; }
		public decimal? ScoreMaximum { get; set; }
		public string ActivityProgress { get; set; }
		public string GradingProgress { get; set; }
		public string Comment { get; set; }
		public DateTime? Timestamp { get; set; }

		public string ToJson() => JsonConvert.SerializeObject(this);

		public static LmsLtiScoreState Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new LmsLtiScoreState();
			try
			{
				return JsonConvert.DeserializeObject<LmsLtiScoreState>(json) ?? new LmsLtiScoreState();
			}
			catch (JsonException)
			{
				return new LmsLtiScoreState();
			}
		}
	}

	/// <summary>What the player renders for a SCORM or cmi5 lesson: the sandboxed frame's URL, or why it cannot launch.</summary>
	public sealed class LmsContentLaunch
	{
		public LmsErrors Error { get; set; }

		/// <summary>The content URL (relative to the site): /lms-content/{token}/{href}, with the cmi5 launch parameters for an AU.</summary>
		public string Url { get; set; }

		public LmsContentPackage Package { get; set; }

		public LmsRuntimeRegistration Registration { get; set; }

		/// <summary>The item launched and the package's items (a SCO menu when there are several).</summary>
		public LmsPackageItem Item { get; set; }

		public List<LmsPackageItem> Items { get; set; } = new List<LmsPackageItem>();

		/// <summary>Items satisfied so far (by identifier).</summary>
		public HashSet<string> SatisfiedItems { get; set; } = new HashSet<string>(StringComparer.Ordinal);

		public DateTime? ExpiresOn { get; set; }

		public bool Succeeded => Error == LmsErrors.None && Url != null;
	}

	/// <summary>The content request resolved from its token: which package files it may read and how to wrap HTML.</summary>
	public sealed class LmsContentSession
	{
		public LmsRuntimeSession Session { get; set; }
		public LmsContentPackage Package { get; set; }
	}

	/// <summary>What the SCORM adapter starts with (served as the runtime config script of a launch).</summary>
	public sealed class LmsScormLaunchConfig
	{
		/// <summary>"1.2" or "2004".</summary>
		public string Version { get; set; }

		public string CommitUrl { get; set; }

		/// <summary>Every value the adapter answers GetValue with at start: stored writable values and the read-only ones.</summary>
		public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
	}

	/// <summary>One commit from the SCORM adapter.</summary>
	public sealed class LmsScormCommit
	{
		public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>LMSFinish / Terminate: the session ends (its session time is added once).</summary>
		public bool Finish { get; set; }
	}

	/// <summary>The outcome of a runtime write (a SCORM commit, a cmi5 statement, an LTI score).</summary>
	public sealed class LmsRuntimeOutcome
	{
		public LmsErrors Error { get; set; }

		/// <summary>An HTTP-shaped refusal for the runtime endpoints: 400, 401, 403, 404, 409.</summary>
		public int Status { get; set; } = 200;

		public LmsRuntimeRegistration Registration { get; set; }

		/// <summary>The lesson became satisfied with this write.</summary>
		public bool LessonSatisfied { get; set; }

		/// <summary>The course completed with this write.</summary>
		public bool CourseCompleted { get; set; }

		/// <summary>Values the caller returns (statement ids for an LRS POST, the auth token for a cmi5 fetch, a JSON document).</summary>
		public object Value { get; set; }

		public bool Succeeded => Error == LmsErrors.None && Status < 400;

		public static LmsRuntimeOutcome Refuse(int status, LmsErrors error = LmsErrors.InvalidInput) => new LmsRuntimeOutcome { Status = status, Error = error };
	}

	/// <summary>An LTI launch step: where the browser goes next (the tool's login URL with its query, or the form post with the id_token).</summary>
	public sealed class LmsLtiLaunchStep
	{
		public LmsErrors Error { get; set; }

		/// <summary>The URL to redirect to (login initiation) or to post the form to (the tool's redirect URI).</summary>
		public string Url { get; set; }

		/// <summary>Form fields (id_token and state) for the auto-submitted POST; empty for a redirect.</summary>
		public Dictionary<string, string> Form { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

		public bool Succeeded => Error == LmsErrors.None && Url != null;
	}

	/// <summary>A token endpoint answer (OAuth 2 client credentials).</summary>
	public sealed class LmsLtiTokenResult
	{
		/// <summary>OAuth error code ("invalid_client", "invalid_grant", "invalid_scope", "invalid_request"), null on success.</summary>
		public string OAuthError { get; set; }

		public string AccessToken { get; set; }

		public string Scope { get; set; }

		public int ExpiresIn { get; set; }

		public bool Succeeded => OAuthError == null && AccessToken != null;
	}

	/// <summary>The platform details an administrator gives a tool when registering Resgrid with it.</summary>
	public sealed class LmsLtiPlatformDetails
	{
		public string Issuer { get; set; }
		public string JwksUrl { get; set; }
		public string AuthorizationUrl { get; set; }
		public string TokenUrl { get; set; }
		public string ActiveKid { get; set; }
	}

	/// <summary>A tool registration as the editor saves it.</summary>
	public sealed class LmsLtiToolInput
	{
		public int LmsLtiToolId { get; set; }
		public string Name { get; set; }
		public string OidcLoginUrl { get; set; }
		public string LaunchUrl { get; set; }
		public string RedirectUris { get; set; }
		public string JwksUrl { get; set; }
		public string PublicJwksJson { get; set; }
		public string CustomParameters { get; set; }
		public bool AllowGradePassback { get; set; }
		public bool ShareName { get; set; }
		public bool ShareEmail { get; set; }
		public bool IsActive { get; set; } = true;
	}

	/// <summary>The xAPI page: the settings (secret never shown) and the outbox counts.</summary>
	public sealed class LmsXapiOverview
	{
		public LmsStandardsSettings Settings { get; set; }
		public bool HasSecret { get; set; }
		public int Pending { get; set; }
		public int Failed { get; set; }
		public int Delivered { get; set; }
		public List<LmsXapiOutboxItem> RecentFailures { get; set; } = new List<LmsXapiOutboxItem>();
	}

	/// <summary>A learning event worth an xAPI statement.</summary>
	public sealed class LmsXapiEvent
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }

		/// <summary>One of <see cref="LmsXapiVerbs"/>.</summary>
		public string VerbId { get; set; }

		public int LmsCourseId { get; set; }
		public string CourseTitle { get; set; }

		/// <summary>Set for a lesson-level statement (an assessment, a SCORM / cmi5 / LTI lesson).</summary>
		public int? LmsLessonId { get; set; }
		public string LessonTitle { get; set; }

		public bool? Completion { get; set; }
		public bool? Success { get; set; }

		/// <summary>0–100.</summary>
		public decimal? ScorePercent { get; set; }

		public int? DurationSeconds { get; set; }

		public DateTime OccurredOn { get; set; }
	}

	/// <summary>The xAPI verbs the outbox sends (ADL vocabulary).</summary>
	public static class LmsXapiVerbs
	{
		public const string Completed = "http://adlnet.gov/expapi/verbs/completed";
		public const string Passed = "http://adlnet.gov/expapi/verbs/passed";
		public const string Failed = "http://adlnet.gov/expapi/verbs/failed";
		public const string Attempted = "http://adlnet.gov/expapi/verbs/attempted";

		public static string Display(string verbId)
		{
			switch (verbId)
			{
				case Completed: return "completed";
				case Passed: return "passed";
				case Failed: return "failed";
				case Attempted: return "attempted";
				default: return null;
			}
		}
	}
}
