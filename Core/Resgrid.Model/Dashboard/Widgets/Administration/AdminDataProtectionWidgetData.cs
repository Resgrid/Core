using System;

namespace Resgrid.Model.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.dataProtection (plan §7.11, §9A.5): Advanced Data Protection state and migration progress. State only — never a
	/// protected value. (Reveal / break-glass counts are omitted: the audit store has no cheap count.)
	/// </summary>
	public sealed class AdminDataProtectionWidgetData
	{
		/// <summary>DepartmentDataProtectionState name (Disabled, EnrollmentQueued, Encrypting, Enabled…) — W.admin.dataProtection.State.{name}.</summary>
		public string State { get; set; }

		/// <summary>Protection is enforced (reads of catalogued fields need a grant).</summary>
		public bool Enforced { get; set; }

		public int CatalogVersion { get; set; }

		public bool CatalogUpgradePending { get; set; }

		/// <summary>The step-up window members get after verifying, in minutes.</summary>
		public int StepUpWindowMinutes { get; set; }

		public DateTime? OffboardingEffectiveOn { get; set; }

		public bool MigrationRunning { get; set; }

		/// <summary>DepartmentDataProtectionMigrationKind name while a migration runs.</summary>
		public string MigrationKind { get; set; }

		public int? PercentComplete { get; set; }

		public long RowsTotal { get; set; }

		public long RowsCompleted { get; set; }

		public long RowsAnomalous { get; set; }
	}
}
