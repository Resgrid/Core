using System;
using System.Threading.Tasks;

namespace Resgrid.Model
{
	/// <summary>
	/// Everything the migration engine needs to run one department's nightly window. The coordinator
	/// owns the department operation lock; the engine calls <see cref="HeartbeatAsync"/> at least once
	/// per batch so the lock's safety valve keeps sliding while real work happens, and stops at
	/// <see cref="WindowEndUtc"/> by checkpointing, never mid-batch.
	/// </summary>
	public sealed class AdpMigrationNightContext
	{
		public int DepartmentId { get; set; }

		/// <summary>DepartmentDataProtectionMigrationKind value for this run.</summary>
		public DepartmentDataProtectionMigrationKind Kind { get; set; }

		/// <summary>Catalog version this run migrates to.</summary>
		public int CatalogVersion { get; set; }

		/// <summary>Target department key version for enrollment/rotation; null for offboarding.</summary>
		public int? TargetKeyVersion { get; set; }

		/// <summary>UTC instant the department's overnight window closes.</summary>
		public DateTime WindowEndUtc { get; set; }

		/// <summary>The active DepartmentOperationLocks row id held by the coordinator.</summary>
		public int DepartmentOperationLockId { get; set; }

		/// <summary>Advances the lock heartbeat; the engine must invoke it at least once per batch.</summary>
		public Func<Task> HeartbeatAsync { get; set; }

		/// <summary>Correlation id threaded through migration rows, audit and notifications.</summary>
		public string CorrelationId { get; set; }
	}
}
