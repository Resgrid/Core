using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// One RMS PermissionTypes row as the admin Permissions screen, ClaimsLogic.AddRecordClaims, and the
	/// activation-time row migration all understand it (Identifier Allocation Registry section 4.1).
	/// </summary>
	public sealed class RecordPermissionDescriptor
	{
		public RecordPermissionDescriptor(PermissionTypes type, PermissionActions noRowDefault, bool lockToGroupMeaningful, string summary, bool everyoneOffered = true)
		{
			Type = type;
			NoRowDefault = noRowDefault;
			LockToGroupMeaningful = lockToGroupMeaningful;
			Summary = summary;
			EveryoneOffered = everyoneOffered;
		}

		public PermissionTypes Type { get; }

		/// <summary>
		/// The PermissionActions value the chain evaluates when the department has no Permission row for
		/// this type. Must equal the pre-activation Logs behavior for the parity types.
		/// </summary>
		public PermissionActions NoRowDefault { get; }

		/// <summary>Whether the Permissions screen exposes LockToGroup on this row.</summary>
		public bool LockToGroupMeaningful { get; }

		/// <summary>
		/// Whether the Permissions screen offers "Everyone" on this row. False for the permissions the plan
		/// anchors to department administrators (submission, external sharing, restricted sections, definitions,
		/// reports, disclosures, legal hold): they stay grantable onward to groups and roles, never to everyone.
		/// </summary>
		public bool EveryoneOffered { get; }

		public string Summary { get; }
	}

	/// <summary>
	/// The single source of no-row defaults for PermissionTypes 50-67. ClaimsLogic reads it to derive
	/// claims, the Permissions admin screen reads it to render the Records block, and the activation
	/// command reads it to show the administrator the before/after table.
	/// </summary>
	public static class RecordPermissionCatalog
	{
		public static readonly IReadOnlyList<RecordPermissionDescriptor> All = new List<RecordPermissionDescriptor>
		{
			new RecordPermissionDescriptor(PermissionTypes.CreateRecord, PermissionActions.Everyone, true, "Author Records"),
			new RecordPermissionDescriptor(PermissionTypes.DeleteRecord, PermissionActions.Everyone, true, "Void or cancel Records"),
			new RecordPermissionDescriptor(PermissionTypes.ReviewRecords, PermissionActions.DepartmentAndGroupAdmins, true, "Review Records"),
			new RecordPermissionDescriptor(PermissionTypes.ApproveRecords, PermissionActions.DepartmentAdminsOnly, true, "Approve Records"),
			new RecordPermissionDescriptor(PermissionTypes.FinalizeRecords, PermissionActions.Everyone, true, "Finalize Records"),
			new RecordPermissionDescriptor(PermissionTypes.AmendRecords, PermissionActions.DepartmentAndGroupAdmins, true, "Amend finalized Records"),
			new RecordPermissionDescriptor(PermissionTypes.SubmitRecords, PermissionActions.DepartmentAdminsOnly, false, "Submit Records to a reporting destination", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ExportRecords, PermissionActions.Everyone, true, "Print and export Records"),
			new RecordPermissionDescriptor(PermissionTypes.ShareRecordsExternally, PermissionActions.DepartmentAdminsOnly, false, "Share Records with other groups or externally", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ViewRestrictedRecords, PermissionActions.DepartmentAdminsOnly, true, "View restricted Record sections", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ViewLegacyRecords, PermissionActions.Everyone, true, "View legacy Logs history"),
			new RecordPermissionDescriptor(PermissionTypes.ViewGroupRecords, PermissionActions.Everyone, true, "See other groups' Records"),
			new RecordPermissionDescriptor(PermissionTypes.ManageRecordDefinitions, PermissionActions.DepartmentAdminsOnly, false, "Manage Record definitions", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.PublishRecordDefinitions, PermissionActions.DepartmentAdminsOnly, false, "Publish Record definitions", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ManageRecordReports, PermissionActions.DepartmentAdminsOnly, false, "Manage saved Record reports", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ManageRecordDisclosures, PermissionActions.DepartmentAdminsOnly, false, "Manage public-records disclosures", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ManageRecordLegalHold, PermissionActions.DepartmentAdminsOnly, false, "Place and release legal holds", everyoneOffered: false),
			new RecordPermissionDescriptor(PermissionTypes.ReassignRecordDrafts, PermissionActions.DepartmentAndGroupAdmins, true, "Reassign draft Records")
		};

		public const int FirstValue = 50;
		public const int LastValue = 67;

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
