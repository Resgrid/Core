using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// What a Field Records rollout event describes (RMS plan RMS-1D, "per-app rollout dashboards"). The set is
	/// closed so a dashboard can count it; anything else is refused rather than stored as a free-text label.
	/// </summary>
	public static class RmsFieldRolloutEventTypes
	{
		/// <summary>The app asked whether Records is usable for it at this version.</summary>
		public const string Preflight = "preflight";
		/// <summary>The app asked for its catalog in a context.</summary>
		public const string Catalog = "catalog";
		/// <summary>A Record was started from the catalog.</summary>
		public const string DraftStarted = "draft_started";
		/// <summary>A draft was pushed to the server.</summary>
		public const string DraftSaved = "draft_saved";
		/// <summary>A sync bundle was pulled.</summary>
		public const string Sync = "sync";
		/// <summary>A conflict was presented to a person.</summary>
		public const string Conflict = "conflict";
		/// <summary>An attachment upload finished, failed or was abandoned.</summary>
		public const string Attachment = "attachment";
		/// <summary>A Record reached a lifecycle end in the app: submitted or finalized.</summary>
		public const string Completed = "completed";
		/// <summary>Authoring was left without saving or sending.</summary>
		public const string Abandoned = "abandoned";
		/// <summary>The person gave up on the app for this Record and was pointed at the web app.</summary>
		public const string WebHandoff = "web_handoff";

		public static readonly IReadOnlyList<string> All = new[] { Preflight, Catalog, DraftStarted, DraftSaved, Sync, Conflict, Attachment, Completed, Abandoned, WebHandoff };

		public static bool IsKnown(string eventType) => eventType != null && All.Contains(eventType.Trim().ToLowerInvariant());
	}

	/// <summary>
	/// One safe rollout datapoint from a field app (RMS plan RMS-1D). Identifiers, counts, durations and outcome
	/// codes only: no field values, no free text, no location, nothing that could carry record content out of the
	/// aggregate. Rows are department-scoped and read only through the rollout dashboard.
	/// </summary>
	public class RmsFieldRolloutEvent : IEntity
	{
		public string RmsFieldRolloutEventId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary><see cref="RmsOriginClient"/> — which app.</summary>
		public int OriginClient { get; set; }

		public string AppVersion { get; set; }

		/// <summary>The renderer capability the client reported (records.v1 / v1b / v1c).</summary>
		public string ClientCapability { get; set; }

		/// <summary><see cref="RmsFieldRolloutEventTypes"/>.</summary>
		public string EventType { get; set; }

		/// <summary>A coded outcome: ok, or the refusal/conflict code the client was given.</summary>
		public string Outcome { get; set; }

		public string DefinitionKey { get; set; }

		public int? DefinitionVersion { get; set; }

		public string RecordId { get; set; }

		public string UserId { get; set; }

		/// <summary>Time-to-complete and similar durations, in milliseconds.</summary>
		public long? DurationMs { get; set; }

		/// <summary>A count the event carries: rows synced, attachment bytes in kilobytes, retries.</summary>
		public int? ItemCount { get; set; }

		public DateTime OccurredOn { get; set; }

		public DateTime RecordedOn { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return RmsFieldRolloutEventId; }
			set { RmsFieldRolloutEventId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsFieldRolloutEvents";

		[NotMapped]
		public string IdName => "RmsFieldRolloutEventId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One event as a client reports it; the server supplies department, user and the recorded time.</summary>
	public class RecordFieldRolloutInput
	{
		public string EventType { get; set; }
		public string Outcome { get; set; }
		public string DefinitionKey { get; set; }
		public int? DefinitionVersion { get; set; }
		public string RecordId { get; set; }
		public long? DurationMs { get; set; }
		public int? ItemCount { get; set; }
		public DateTime? OccurredOn { get; set; }
	}

	/// <summary>A batch from one app at one version; batching keeps a busy shift from making a request per tap.</summary>
	public class RecordFieldRolloutBatch
	{
		/// <summary>Most events one batch may carry; a longer report is truncated rather than refused.</summary>
		public const int MaxEvents = 100;

		public RmsOriginClient OriginClient { get; set; } = RmsOriginClient.Api;
		public string AppVersion { get; set; }
		public string ClientCapability { get; set; }
		public List<RecordFieldRolloutInput> Events { get; set; } = new List<RecordFieldRolloutInput>();
	}

	/// <summary>Adoption and outcomes for one app over the dashboard window.</summary>
	public class RecordsFieldRolloutApp
	{
		public string OriginClient { get; set; }
		/// <summary>Distinct members who used Records in this app in the window.</summary>
		public int ActiveUsers { get; set; }
		/// <summary>Version string to the number of members reporting it, newest-reported first.</summary>
		public List<RecordsFieldRolloutVersion> Versions { get; set; } = new List<RecordsFieldRolloutVersion>();
		/// <summary>Members whose reported version met the department's minimum at the time they reported it.</summary>
		public int CompatibleUsers { get; set; }
		public int CatalogRequests { get; set; }
		public int CatalogFailures { get; set; }
		/// <summary>Refusal code to the number of times the server gave it.</summary>
		public Dictionary<string, int> CatalogFailureReasons { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		public int DraftsStarted { get; set; }
		public int DraftsSaved { get; set; }
		public int DraftSaveFailures { get; set; }
		public int Syncs { get; set; }
		public int SyncFailures { get; set; }
		public int Conflicts { get; set; }
		public Dictionary<string, int> ConflictKinds { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		public int AttachmentsUploaded { get; set; }
		public int AttachmentFailures { get; set; }
		public int Completed { get; set; }
		public int Abandoned { get; set; }
		public int WebHandoffs { get; set; }
		/// <summary>Median milliseconds from starting a Record to submitting or finalizing it.</summary>
		public long? MedianTimeToCompleteMs { get; set; }
		/// <summary>Records this app actually created in the window, counted from the Records themselves.</summary>
		public int RecordsCreated { get; set; }
		public int RecordsFinalized { get; set; }

		/// <summary>Abandonment over the drafts started in the window; null when nothing was started.</summary>
		public double? AbandonmentRate => DraftsStarted == 0 ? (double?)null : Math.Round((double)Abandoned / DraftsStarted, 3);
	}

	public class RecordsFieldRolloutVersion
	{
		public string AppVersion { get; set; }
		public int Users { get; set; }
		public int Events { get; set; }
	}

	/// <summary>The per-app rollout dashboard (RMS plan RMS-1D).</summary>
	public class RecordsFieldRollout
	{
		public DateTime GeneratedOn { get; set; } = DateTime.UtcNow;
		public int WindowDays { get; set; }
		public DateTime WindowStart { get; set; }
		/// <summary>The minimum version each app must report, from RecordsFieldConfig; blank means any.</summary>
		public Dictionary<string, string> MinimumAppVersions { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public List<RecordsFieldRolloutApp> Apps { get; set; } = new List<RecordsFieldRolloutApp>();
		/// <summary>True when the department has at least one field app flag on.</summary>
		public bool AnyAppEnabled { get; set; }
		public Dictionary<string, bool> AppFlags { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
	}
}
