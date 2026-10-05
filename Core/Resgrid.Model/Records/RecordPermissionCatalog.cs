using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// One RMS PermissionTypes row as the admin Permissions screen, ClaimsLogic.AddRecordClaims, and the
	/// activation-time row migration all understand it (Identifier Allocation Registry section 4.1).
	/// </summary>
	public sealed class RecordPermissionDescriptor
	{
		public RecordPermissionDescriptor(PermissionTypes type, PermissionActions noRowDefault, bool lockToGroupMeaningful, string summary, bool everyoneOffered = true,
			bool noRowLockToGroup = false, bool shownOnSecurityScreen = true)
		{
			Type = type;
			NoRowDefault = noRowDefault;
			LockToGroupMeaningful = lockToGroupMeaningful;
			Summary = summary;
			EveryoneOffered = everyoneOffered;
			NoRowLockToGroup = noRowLockToGroup;
			ShownOnSecurityScreen = shownOnSecurityScreen;
		}

		public PermissionTypes Type { get; }

		/// <summary>
		/// The PermissionActions value the chain evaluates when the department has no Permission row for
		/// this type. Must equal the pre-activation Logs behavior for the parity types.
		/// </summary>
		public PermissionActions NoRowDefault { get; }

		/// <summary>
		/// Whether the Permissions screen exposes LockToGroup on this row. True only where an authorization check reads the
		/// lock; a box that is saved but never evaluated would promise a group restriction that does not exist.
		/// </summary>
		public bool LockToGroupMeaningful { get; }

		/// <summary>
		/// Whether the Permissions screen shows this row at all (and so whether SetPermission accepts it). False for a
		/// value whose claim is still issued for capability reporting but which no endpoint enforces yet; its no-row
		/// default keeps applying and a previously stored row still loads.
		/// </summary>
		public bool ShownOnSecurityScreen { get; }

		/// <summary>
		/// The group lock the authorization service applies when the department has no Permission row for this type
		/// (ChecklistAuthorizationService for ViewChecklistResults, WorkOrderAuthorizationService for ViewAllWorkOrders).
		/// The Permissions screen ticks the group-only box from it so an unsaved row shows the lock that is in force.
		/// </summary>
		public bool NoRowLockToGroup { get; }

		/// <summary>
		/// Whether the Permissions screen offers "Everyone" on this row. False for the permissions the plan
		/// anchors to department administrators (submission, external sharing, restricted sections, definitions,
		/// reports, disclosures, legal hold): they stay grantable onward to groups and roles, never to everyone.
		/// </summary>
		public bool EveryoneOffered { get; }

		public string Summary { get; }
	}

	/// <summary>
	/// The single source of no-row defaults for PermissionTypes 50-67. Of the Records rows only ViewGroupRecords offers
	/// the group-only box (RecordsAuthorizationService reads its lock); ShareRecordsExternally and ViewLegacyRecords keep
	/// issuing their claims but are not on the Permissions screen until an endpoint enforces them. ClaimsLogic reads it to derive
	/// claims, the Permissions admin screen reads it to render the Records block, and the activation
	/// command reads it to show the administrator the before/after table.
	/// </summary>
	public static class RecordPermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new List<RecordPermissionDescriptor>
		{
			new RecordPermissionDescriptor(PermissionTypes.CreateRecord, PermissionActions.Everyone, false, "Author Records"),
			new RecordPermissionDescriptor(PermissionTypes.DeleteRecord, PermissionActions.Everyone, false, "Void or cancel Records"),
			new RecordPermissionDescriptor(PermissionTypes.ReviewRecords, PermissionActions.DepartmentAndGroupAdmins, false, "Review Records"),
			new RecordPermissionDescriptor(PermissionTypes.ApproveRecords, PermissionActions.DepartmentAdminsOnly, false, "Approve Records"),
			new RecordPermissionDescriptor(PermissionTypes.FinalizeRecords, PermissionActions.Everyone, false, "Finalize Records"),
			new RecordPermissionDescriptor(PermissionTypes.AmendRecords, PermissionActions.DepartmentAndGroupAdmins, false, "Amend finalized Records"),
			new RecordPermissionDescriptor(PermissionTypes.SubmitRecords, PermissionActions.DepartmentAdminsOnly, false, "Submit Records to a reporting destination", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ExportRecords, PermissionActions.Everyone, false, "Print and export Records"),
			new RecordPermissionDescriptor(PermissionTypes.ShareRecordsExternally, PermissionActions.DepartmentAdminsOnly, false, "Share Records with other groups or externally", everyoneOffered: false, shownOnSecurityScreen: false),
			new RecordPermissionDescriptor(PermissionTypes.ViewRestrictedRecords, PermissionActions.DepartmentAdminsOnly, false, "View restricted Record sections", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ViewLegacyRecords, PermissionActions.Everyone, false, "View legacy Logs history", shownOnSecurityScreen: false),
			new RecordPermissionDescriptor(PermissionTypes.ViewGroupRecords, PermissionActions.Everyone, true, "See other groups' Records"),
			new RecordPermissionDescriptor(PermissionTypes.ManageRecordDefinitions, PermissionActions.DepartmentAdminsOnly, false, "Manage Record definitions", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.PublishRecordDefinitions, PermissionActions.DepartmentAdminsOnly, false, "Publish Record definitions", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ManageRecordReports, PermissionActions.DepartmentAdminsOnly, false, "Manage saved Record reports", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ManageRecordDisclosures, PermissionActions.DepartmentAdminsOnly, false, "Manage public-records disclosures", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ManageRecordLegalHold, PermissionActions.DepartmentAdminsOnly, false, "Place and release legal holds", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ReassignRecordDrafts, PermissionActions.DepartmentAndGroupAdmins, false, "Reassign draft Records"),
			new RecordPermissionDescriptor(PermissionTypes.RecordsPreventionAdmin, PermissionActions.DepartmentAdminsOnly, false, "Manage prevention data (occupancies, inspections, hydrants, permits, CRR)", everyoneOffered: false)
		};

		public const int FirstValue = 50;
		/// <summary>Highest RMS value. 68 (Unified Search's ManageSearchIndex) sits inside the range and is not a Records permission; iterate <see cref="All"/>, never the range.</summary>
		public const int LastValue = 69;

		public static RecordPermissionDescriptor Get(PermissionTypes type)
		{
			foreach (var descriptor in All)
			{
				if (descriptor.Type == type)
					return descriptor;
			}

			return null;
		}

		/// <summary>
		/// The activation-time Permission-row migration (registry section 4.6): which legacy row seeds
		/// which Records rows. Action, Data and LockToGroup are copied verbatim. ViewGroupUsers is read
		/// as a suggestion for ViewGroupRecords but never applied silently, so it is not listed here.
		/// </summary>
		public static readonly IReadOnlyList<KeyValuePair<PermissionTypes, PermissionTypes[]>> ActivationRowMapping =
			new List<KeyValuePair<PermissionTypes, PermissionTypes[]>>
			{
				new KeyValuePair<PermissionTypes, PermissionTypes[]>(PermissionTypes.CreateLog, new[] { PermissionTypes.CreateRecord, PermissionTypes.FinalizeRecords }),
				new KeyValuePair<PermissionTypes, PermissionTypes[]>(PermissionTypes.DeleteLog, new[] { PermissionTypes.DeleteRecord })
			};
	}
}
