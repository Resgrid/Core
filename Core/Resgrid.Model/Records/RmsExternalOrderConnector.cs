using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// The ordering systems a connector can speak for (RMS plan section 4.1, "External ordering system
	/// connectors"). Every provider consumes the same documented Resgrid Mutual-Aid Order Feed; the provider
	/// fixes the identifier scheme, the default deployment profile and which identifiers are mandatory, so an
	/// IROC feed that omits a resource-order number is refused rather than guessed at.
	/// </summary>
	public static class RmsExternalOrderConnectorProviders
	{
		/// <summary>U.S. wildland: IROC resource orders and requests, NWCG code schemes.</summary>
		public const string Iroc = "iroc-feed";
		/// <summary>Canada wildland: CIFFC / member-agency exchanges under MARS.</summary>
		public const string Ciffc = "ciffc-feed";
		/// <summary>A member agency's own ordering system publishing the feed under an opaque agency scheme.</summary>
		public const string Agency = "agency-feed";
		/// <summary>Any all-hazard or local mutual-aid system publishing the feed with opaque identifiers.</summary>
		public const string Generic = "generic-feed";
		public static readonly IReadOnlyList<string> All = new[] { Iroc, Ciffc, Agency, Generic };
		public static bool IsKnown(string key) => key != null && All.Contains(key.Trim().ToLowerInvariant());
	}

	/// <summary>How the connector presents itself to the source.</summary>
	public static class RmsConnectorCredentialKinds
	{
		public const string None = "none";
		public const string Bearer = "bearer";
		public const string Header = "header";
		public static readonly IReadOnlyList<string> All = new[] { None, Bearer, Header };
		public static bool IsKnown(string kind) => kind != null && All.Contains(kind.Trim().ToLowerInvariant());
	}

	/// <summary>Who owns an external order's facts in Resgrid: a person who keyed it, or a connector that imports it.</summary>
	public static class RmsExternalOrderOwnership
	{
		public const string Manual = "manual";
		public const string Connector = "connector";
	}

	public static class RmsConnectorRunTriggers
	{
		public const string Poll = "poll";
		public const string Manual = "manual";
		public const string Inbound = "inbound";
	}

	public static class RmsConnectorRunOutcomes
	{
		public const string Ok = "ok";
		public const string Failed = "failed";
		public const string RateLimited = "rate_limited";
		public const string Disabled = "disabled";
		public const string Rejected = "rejected";
	}

	/// <summary>
	/// One department's connection to one external ordering system (RMS plan section 4.1). The connector holds
	/// exactly what the plan demands before any connector may exist: a documented API (the feed contract), a
	/// credential held encrypted, a rate limit, an acknowledgement of the source's terms, and explicit read and
	/// write authority. Write authority cannot be granted in this release; the property exists so the refusal is
	/// a recorded decision rather than an absence. Import never overwrites signed deployment history: every change
	/// from the source lands as a new versioned snapshot, and disagreements go to reconciliation for a person.
	/// </summary>
	public class RmsExternalOrderConnector : IEntity
	{
		public string RmsExternalOrderConnectorId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary><see cref="RmsExternalOrderConnectorProviders"/>.</summary>
		public string ProviderKey { get; set; }

		public string Name { get; set; }

		/// <summary>The ordering system as the department names it (IROC, CIFFC, an agency's system).</summary>
		public string SourceSystem { get; set; }

		/// <summary>Identifier scheme stamped on every order this connector imports (iroc, ciffc, agency:&lt;code&gt;, local).</summary>
		public string SourceScheme { get; set; }

		/// <summary>The deployment profile orders from this source are provisioned under (<see cref="RmsDeploymentProfiles"/>).</summary>
		public string ProfileKey { get; set; }

		/// <summary>https root of the feed. http is refused outside development.</summary>
		public string BaseUrl { get; set; }

		/// <summary><see cref="RmsConnectorCredentialKinds"/>.</summary>
		public string CredentialKind { get; set; }

		/// <summary>Header name for the header credential kind; ignored otherwise.</summary>
		public string CredentialHeaderName { get; set; }

		/// <summary>The outbound credential, encrypted under RecordsConnectorConfig.CredentialPassphrase. Never returned by any read.</summary>
		public string CredentialCiphertext { get; set; }

		/// <summary>SHA-256 of the inbound push token. The token itself is shown once at creation or rotation and never stored.</summary>
		public string InboundTokenHash { get; set; }

		/// <summary>The department authorizes reading from the source.</summary>
		public bool ReadEnabled { get; set; }

		/// <summary>The department authorizes writing back to the source. Always false in this release (plan: "no P0 external writes").</summary>
		public bool WriteEnabled { get; set; }

		public int PollIntervalMinutes { get; set; }

		public int MaxRequestsPerHour { get; set; }

		public int RequestsThisHour { get; set; }

		public DateTime? RateWindowStartedOn { get; set; }

		/// <summary>Where the source's terms of use live, so the acknowledgement names what was agreed to.</summary>
		public string TermsReference { get; set; }

		public DateTime? TermsAcknowledgedOn { get; set; }

		public string TermsAcknowledgedByUserId { get; set; }

		public bool IsEnabled { get; set; }

		/// <summary>Opaque resume cursor the feed handed back last time.</summary>
		public string LastCursor { get; set; }

		public DateTime? LastPolledOn { get; set; }

		public DateTime? LastSuccessOn { get; set; }

		public string LastError { get; set; }

		public int ConsecutiveFailures { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		public string ModifiedByUserId { get; set; }

		[Key]
		[Required]
		public long RowVersion { get; set; }

		public DateTime? DeletedOn { get; set; }

		/// <summary>Everything the plan requires before a connector may run.</summary>
		[NotMapped]
		public bool IsReadyToRun => IsEnabled && ReadEnabled && TermsAcknowledgedOn.HasValue && !DeletedOn.HasValue && !string.IsNullOrWhiteSpace(BaseUrl);

		[NotMapped]
		public object IdValue
		{
			get { return RmsExternalOrderConnectorId; }
			set { RmsExternalOrderConnectorId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsExternalOrderConnectors";

		[NotMapped]
		public string IdName => "RmsExternalOrderConnectorId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsReadyToRun" };
	}

	/// <summary>One import pass: what was read, what changed, and what needs a person. Counts and codes only.</summary>
	public class RmsExternalOrderConnectorRun : IEntity
	{
		public string RmsExternalOrderConnectorRunId { get; set; }

		public int DepartmentId { get; set; }

		public string RmsExternalOrderConnectorId { get; set; }

		/// <summary><see cref="RmsConnectorRunTriggers"/>.</summary>
		public string Trigger { get; set; }

		public string TriggeredByUserId { get; set; }

		public DateTime StartedOn { get; set; }

		public DateTime? FinishedOn { get; set; }

		/// <summary><see cref="RmsConnectorRunOutcomes"/>.</summary>
		public string Outcome { get; set; }

		public string Error { get; set; }

		public int RequestCount { get; set; }

		public int OrdersSeen { get; set; }

		public int OrdersCreated { get; set; }

		public int SnapshotsRecorded { get; set; }

		public int RequestsAdded { get; set; }

		public int Unchanged { get; set; }

		public int Rejected { get; set; }

		public int Conflicts { get; set; }

		/// <summary>The feed's declared source version, for the run log.</summary>
		public string SourceVersion { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return RmsExternalOrderConnectorRunId; }
			set { RmsExternalOrderConnectorRunId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsExternalOrderConnectorRuns";

		[NotMapped]
		public string IdName => "RmsExternalOrderConnectorRunId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Create or update a connector. The credential is write-only; leaving it null on update keeps the stored one.</summary>
	public class RecordDeploymentConnectorInput
	{
		public string ProviderKey { get; set; } = RmsExternalOrderConnectorProviders.Generic;
		public string Name { get; set; }
		public string SourceSystem { get; set; }
		public string SourceScheme { get; set; }
		public string ProfileKey { get; set; }
		public string BaseUrl { get; set; }
		public string CredentialKind { get; set; } = RmsConnectorCredentialKinds.None;
		public string CredentialHeaderName { get; set; }
		/// <summary>The outbound secret. Encrypted at rest; never echoed back.</summary>
		public string Credential { get; set; }
		public bool ReadEnabled { get; set; } = true;
		/// <summary>Refused when true: this release grants no write authority.</summary>
		public bool WriteEnabled { get; set; }
		public int PollIntervalMinutes { get; set; } = 60;
		public int MaxRequestsPerHour { get; set; } = 12;
		public string TermsReference { get; set; }
	}

	/// <summary>The connector as created plus the one-time inbound token; the token is not recoverable later.</summary>
	public class RecordDeploymentConnectorCreated
	{
		public RmsExternalOrderConnector Connector { get; set; }
		public string InboundToken { get; set; }
	}

	/// <summary>A disagreement between the source's latest snapshot and what the department recorded. Shown, never applied.</summary>
	public class RecordDeploymentReconciliationItem
	{
		public const string SourceReleasedLocalOut = "source_released_local_not_returned";
		public const string SourceClosedLocalOpen = "source_closed_local_open";
		public const string SourceRequestMissingLocally = "source_request_missing_locally";
		public const string LocalFillMissingInSource = "local_fill_missing_in_source";
		public const string SourceStatusAhead = "source_status_ahead";
		public const string SourceStatusBehind = "source_status_behind";

		public string ConnectorId { get; set; }
		public string OrderId { get; set; }
		public string RecordId { get; set; }
		public string OrderNumber { get; set; }
		public string RequestNumber { get; set; }
		public string Kind { get; set; }
		public string SourceStatus { get; set; }
		public string LocalStatus { get; set; }
		public string SourceVersion { get; set; }
		public DateTime? SourceCapturedOn { get; set; }
	}

	/// <summary>The outcome of one connector run, as returned to whoever triggered it.</summary>
	public class RecordDeploymentConnectorRunResult
	{
		public RmsExternalOrderConnectorRun Run { get; set; }
		public List<string> Messages { get; set; } = new List<string>();
	}
}
