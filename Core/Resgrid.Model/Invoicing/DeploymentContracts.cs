using System;
using System.Collections.Generic;

namespace Resgrid.Model.Invoicing
{
	// Workforce & Business Operations plan, Phase C (C4): inputs and results of the deployment core services.

	/// <summary>Input for <c>CreateFromExternalOrderAsync</c> (plan C4, decision 39).</summary>
	public sealed class ExternalOrderDeploymentInput
	{
		public string RmsExternalOrderId { get; set; }
		public DeploymentFinanceModes FinanceMode { get; set; } = DeploymentFinanceModes.OperationalOnly;
		/// <summary>Create a Call for the deployment (the department's choice; the Records deployment already has its own identity).</summary>
		public bool CreateCall { get; set; }
		public string CallType { get; set; }
		public int CallPriority { get; set; }
		/// <summary>Seat the accepted/mobilized/checked-in/assigned fills' AssignedUserId/AssignedUnitId on the roster.</summary>
		public bool PrefillRoster { get; set; } = true;
		public string Name { get; set; }
		public string ContactId { get; set; }
		public string Notes { get; set; }
	}

	/// <summary>A roster warning or conflict (plan C4/C6): the row is still written unless <see cref="Blocking"/>.</summary>
	public sealed class DeploymentRosterWarning
	{
		public const string ScheduleConflict = "schedule_conflict";
		public const string RoleNotHeld = "role_not_held";
		public const string CertificationMissing = "certification_missing";
		public const string CertificationExpiring = "certification_expiring";
		public const string AlreadyRostered = "already_rostered";

		public string Code { get; set; }
		public string SubjectId { get; set; }
		public string Detail { get; set; }
		public bool Blocking { get; set; }
	}

	public sealed class DeploymentRosterResult
	{
		public DeploymentPersonnel Personnel { get; set; }
		public DeploymentUnit Unit { get; set; }
		public DeploymentEquipment Equipment { get; set; }
		public List<DeploymentRosterWarning> Warnings { get; set; } = new List<DeploymentRosterWarning>();
		public bool HasBlockingWarnings => Warnings.Exists(w => w.Blocking);
	}

	public sealed class DeploymentPersonnelInput
	{
		public string UserId { get; set; }
		public string DeploymentUnitId { get; set; }
		public int? UnitRoleId { get; set; }
		public string CertificationCode { get; set; }
		public string CallSign { get; set; }
		public string RmsExternalOrderFillId { get; set; }
		/// <summary>Write the row even when a seat requirement fails (the wizard's partial-fill path, decision 17).</summary>
		public bool Force { get; set; }
	}

	public sealed class DeploymentEquipmentInput
	{
		public string DeploymentUnitId { get; set; }
		public string InventoryAssetId { get; set; }
		public string InventoryItemId { get; set; }
		public string FreeTextName { get; set; }
		public string Notes { get; set; }
		/// <summary>Post an inventory Issue transaction (ReferenceType=Deployment) when the inventory module is present.</summary>
		public bool IssueFromInventory { get; set; }
		public string FromLocationId { get; set; }
	}

	/// <summary>Result of a time-report validation pass (plan C4 ITimeTrackingService).</summary>
	public sealed class TimeReportValidation
	{
		public const string Overlap = "entries_overlap";
		public const string EndBeforeStart = "end_before_start";
		public const string SubjectNotOnRoster = "subject_not_on_roster";
		public const string NoEntries = "no_entries";
		public const string BreakRule = "break_rule";
		public const string LongTravel = "long_travel";
		public const string OutsideReportDate = "outside_report_date";

		public List<TimeReportIssue> Errors { get; set; } = new List<TimeReportIssue>();
		public List<TimeReportIssue> Warnings { get; set; } = new List<TimeReportIssue>();
		public bool IsValid => Errors.Count == 0;
	}

	public sealed class TimeReportIssue
	{
		public string Code { get; set; }
		public string SubjectId { get; set; }
		public string EntryId { get; set; }
		public string Detail { get; set; }
	}

	/// <summary>A saved time report plus the validation that ran on it.</summary>
	public sealed class TimeReportSaveResult
	{
		public DeploymentTimeReport Report { get; set; }
		public TimeReportValidation Validation { get; set; } = new TimeReportValidation();
	}

	/// <summary>The external order and fills behind a deployment, as the RMS pack owns them (decision 39).</summary>
	public sealed class DeploymentExternalContext
	{
		public RmsExternalOrder Order { get; set; }
		public List<RmsExternalOrderFill> Fills { get; set; } = new List<RmsExternalOrderFill>();
	}
}
