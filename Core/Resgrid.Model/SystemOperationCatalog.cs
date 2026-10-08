using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>How a system operation is grouped on the BackOffice System Operations page.</summary>
	public enum SystemOperationCategories
	{
		/// <summary>Rebuilds or drops state the platform keeps in Redis (or another cache).</summary>
		CachedState = 1,

		/// <summary>Runs one of the worker's daily jobs now instead of waiting for its schedule.</summary>
		ScheduledJob = 2
	}

	/// <summary>What one <see cref="SystemOperationTypes"/> value does, for the request validation and the BackOffice page.</summary>
	public sealed class SystemOperationDescriptor
	{
		public SystemOperationDescriptor(SystemOperationTypes type, SystemOperationCategories category, string name, string description,
			bool supportsDepartmentScope, string schedule, int? workerCommandId, bool deletesData)
		{
			Type = type;
			Category = category;
			Name = name;
			Description = description;
			SupportsDepartmentScope = supportsDepartmentScope;
			Schedule = schedule;
			WorkerCommandId = workerCommandId;
			DeletesData = deletesData;
		}

		public SystemOperationTypes Type { get; }

		public SystemOperationCategories Category { get; }

		public string Name { get; }

		public string Description { get; }

		/// <summary>True when a request may name one department; without one it covers every department.</summary>
		public bool SupportsDepartmentScope { get; }

		/// <summary>When the worker runs it on its own (UTC), or null when it only ever runs on request.</summary>
		public string Schedule { get; }

		/// <summary>The scheduled worker command this operation runs early, if any.</summary>
		public int? WorkerCommandId { get; }

		/// <summary>Deletes or purges data when it runs (the same data its schedule would delete).</summary>
		public bool DeletesData { get; }
	}

	/// <summary>
	/// Every operation staff can request from BackOffice -> System Operations. The worker (command 76) runs each one
	/// through SystemOperationRunner; a scheduled job runs exactly the handler its schedule runs, so an early run
	/// behaves like the nightly one.
	/// </summary>
	public static class SystemOperationCatalog
	{
		public static IReadOnlyList<SystemOperationDescriptor> All { get; } = new List<SystemOperationDescriptor>
		{
			new SystemOperationDescriptor(SystemOperationTypes.RebuildSecurityMatrices, SystemOperationCategories.CachedState,
				"Rebuild security matrices",
				"Rebuilds the Redis visibility matrices: who may see each unit, each person, and their locations. Without a matrix, " +
				"visibility checks answer from the permission rows (correct, but slower) and realtime location fan-out recomputes per " +
				"department. The worker queues this for every department on its own when it finds Redis came back empty.",
				supportsDepartmentScope: true, schedule: "Daily 02:00 UTC", workerCommandId: 15, deletesData: false),

			new SystemOperationDescriptor(SystemOperationTypes.ClearDepartmentCaches, SystemOperationCategories.CachedState,
				"Clear department caches",
				"Drops cached department data (department, members, personnel names, plan, groups, call priorities, action logs, " +
				"custom states, latest statuses, feature-flag overrides) so it reloads from the database. Use it when Redis was " +
				"restored from a snapshot, or was unreachable while data changed, and may now serve stale entries.",
				supportsDepartmentScope: true, schedule: null, workerCommandId: null, deletesData: false),

			new SystemOperationDescriptor(SystemOperationTypes.RefreshTtsStaticPrompts, SystemOperationCategories.CachedState,
				"Refresh TTS static prompts",
				"Asks the TTS service to regenerate its static voice prompts (cached in Redis and object storage). Runs only where the " +
				"TTS service URL and admin key are configured.",
				supportsDepartmentScope: false, schedule: "Hourly (TtsConfig.StaticPromptRefreshIntervalMinutes)", workerCommandId: 18, deletesData: false),

			new SystemOperationDescriptor(SystemOperationTypes.PendingDepartmentDeletions, SystemOperationCategories.ScheduledJob,
				"Pending department deletions (System SQL Queue)",
				"Deletes the departments whose deletion request has passed its waiting period. Departments still inside the waiting " +
				"period are not touched.",
				supportsDepartmentScope: false, schedule: "Daily 03:00 UTC", workerCommandId: 14, deletesData: true),

			new SystemOperationDescriptor(SystemOperationTypes.ReportingRollup, SystemOperationCategories.ScheduledJob,
				"Reporting rollup",
				"Writes the previous UTC day's reporting rollup rows for every department.",
				supportsDepartmentScope: false, schedule: "Daily 03:30 UTC", workerCommandId: 21, deletesData: false),

			new SystemOperationDescriptor(SystemOperationTypes.UnitTrackingLocationRetention, SystemOperationCategories.ScheduledJob,
				"Unit tracking location retention",
				"Purges unit tracking locations older than the retention window. Runs only where the retention worker is enabled.",
				supportsDepartmentScope: false, schedule: "Daily 04:30 UTC (UnitTrackingConfig.LocationRetentionHourUtc)", workerCommandId: 24, deletesData: true),

			new SystemOperationDescriptor(SystemOperationTypes.ChatRetention, SystemOperationCategories.ScheduledJob,
				"Chat retention",
				"Purges chat messages past each department's or channel's retention window, and expired chat exports.",
				supportsDepartmentScope: false, schedule: "Daily 04:45 UTC", workerCommandId: 25, deletesData: true),

			new SystemOperationDescriptor(SystemOperationTypes.BidExpiration, SystemOperationCategories.ScheduledJob,
				"Bid expiration",
				"Moves submitted bids past their valid-until date to Expired.",
				supportsDepartmentScope: false, schedule: "Daily 04:00 UTC", workerCommandId: 31, deletesData: false),

			new SystemOperationDescriptor(SystemOperationTypes.DeploymentFinanceReminder, SystemOperationCategories.ScheduledJob,
				"Deployment finance reminder",
				"Sends department admins the daily deployment billing and Cal OES MARS reminder digests. A department this worker " +
				"process already reminded today is skipped; a restarted worker can send a second digest the same day.",
				supportsDepartmentScope: false, schedule: "Daily 04:15 UTC", workerCommandId: 32, deletesData: false),

			new SystemOperationDescriptor(SystemOperationTypes.ComplianceExpiry, SystemOperationCategories.ScheduledJob,
				"Compliance expiry",
				"Expires lapsed service contracts and sends the contract and compliance-document expiry notices. Notices go once per " +
				"day per worker process; a restarted worker can send them again the same day.",
				supportsDepartmentScope: false, schedule: "Daily 04:30 UTC", workerCommandId: 33, deletesData: false),

			new SystemOperationDescriptor(SystemOperationTypes.RmsDueStateEvaluation, SystemOperationCategories.ScheduledJob,
				"RMS due state evaluation",
				"Evaluates RMS record due states (overdue records, due inspections, overdue violations, permit expiry). It emits from " +
				"the persisted due-state rows, so a repeated run stays quiet.",
				supportsDepartmentScope: false, schedule: "Daily 04:00 UTC", workerCommandId: 42, deletesData: false),

			new SystemOperationDescriptor(SystemOperationTypes.RmsRetentionAndPurge, SystemOperationCategories.ScheduledJob,
				"RMS retention and purge",
				"Applies RMS retention and legal holds, purges eligible records and attachments, and rescans attachments the scanner " +
				"could not reach at upload.",
				supportsDepartmentScope: false, schedule: "Daily 03:30 UTC", workerCommandId: 43, deletesData: true),

			new SystemOperationDescriptor(SystemOperationTypes.PayDataReportingReadiness, SystemOperationCategories.ScheduledJob,
				"Pay data reporting readiness",
				"Purges expired pay data export artifacts and, during the filing season, sends the readiness digest (once per day per " +
				"worker process).",
				supportsDepartmentScope: false, schedule: "Daily 04:45 UTC", workerCommandId: 49, deletesData: true),

			new SystemOperationDescriptor(SystemOperationTypes.ProtectedWorkflowSweep, SystemOperationCategories.ScheduledJob,
				"Protected workflow sweep",
				"Expires, revokes and suspends ADP protected workflow releases, and sends the 30- and 7-day expiry notices (each notice " +
				"is recorded, so it goes once).",
				supportsDepartmentScope: false, schedule: "Daily 05:15 UTC", workerCommandId: 71, deletesData: false),

			new SystemOperationDescriptor(SystemOperationTypes.Utf8Cleanup, SystemOperationCategories.ScheduledJob,
				"UTF-8 data cleanup",
				"Repairs text that would block a SQL Server to PostgreSQL move (NUL characters, unpaired surrogates, Windows-1252 " +
				"mojibake). Runs only where the cleanup is enabled.",
				supportsDepartmentScope: false, schedule: "Daily 04:00 UTC (SystemBehaviorConfig.Utf8CleanupHourUtc)", workerCommandId: 22, deletesData: false)
		};

		private static readonly Dictionary<SystemOperationTypes, SystemOperationDescriptor> ByType = All.ToDictionary(x => x.Type);

		/// <summary>The descriptor, or null for a value the catalog does not know.</summary>
		public static SystemOperationDescriptor Get(SystemOperationTypes type)
		{
			return ByType.TryGetValue(type, out var descriptor) ? descriptor : null;
		}

		public static string GetName(int operationType)
		{
			return Get((SystemOperationTypes)operationType)?.Name ?? $"Operation {operationType}";
		}
	}
}
