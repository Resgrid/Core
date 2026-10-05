using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Security
{
	/// <summary>
	/// One row of the Security &gt; Permissions screen: the action values its dropdown offers (in display order),
	/// whether it has a group-only box and a roles picker, and what it shows when the department has no saved row.
	/// </summary>
	public sealed class PermissionScreenEntry
	{
		public PermissionScreenEntry(PermissionTypes type, int[] actions, int noRowValue, bool lockToGroupOffered = false,
			bool noRowLockToGroup = false, bool rolesOffered = true, string notSavedLabelKey = null)
		{
			Type = type;
			Actions = actions;
			NoRowValue = noRowValue;
			LockToGroupOffered = lockToGroupOffered;
			NoRowLockToGroup = noRowLockToGroup;
			RolesOffered = rolesOffered;
			NotSavedLabelKey = notSavedLabelKey;
		}

		public PermissionTypes Type { get; }

		/// <summary>The PermissionActions values the dropdown offers, in display order.</summary>
		public IReadOnlyList<int> Actions { get; }

		/// <summary>
		/// What the dropdown preselects with no saved row: the action value the runtime treats a missing row as, or
		/// <see cref="PermissionScreenCatalog.NotSavedValue"/> when no offered action behaves like a missing row.
		/// </summary>
		public int NoRowValue { get; }

		/// <summary>The group lock the runtime applies with no saved row; the group-only box is ticked from it.</summary>
		public bool NoRowLockToGroup { get; }

		public bool LockToGroupOffered { get; }

		/// <summary>False only for Add/Remove Personnel, whose rows show "no roles" instead of a picker.</summary>
		public bool RolesOffered { get; }

		/// <summary>Security resource for the disabled "not saved" option; set only when NoRowValue is NotSavedValue.</summary>
		public string NotSavedLabelKey { get; }
	}

	/// <summary>
	/// The single description of what the Security &gt; Permissions screen offers. <c>SecurityController.Index</c>
	/// renders the dropdowns, group-only boxes and no-row preselects from it, and <c>SetPermission</c> /
	/// <c>SetPermissionData</c> validate every write against it, so the endpoints can never store a type, action or
	/// lock the screen would not show. Records-family rows come from their permission catalogs, the Advanced Data
	/// Protection rows from <see cref="AdpPermissionDefaults"/>.
	/// </summary>
	public static class PermissionScreenCatalog
	{
		/// <summary>Value of the disabled option shown when the department has no row and no offered action matches it.</summary>
		public const int NotSavedValue = -1;

		public const string NotSavedCreateWorkflowKey = "PermOptionNotSavedCreateWorkflow";
		public const string NotSavedManageWorkflowCredentialsKey = "PermOptionNotSavedManageWorkflowCredentials";
		public const string NotSavedViewWorkflowRunsKey = "PermOptionNotSavedViewWorkflowRuns";

		public static readonly string[] NotSavedLabelKeys = { NotSavedCreateWorkflowKey, NotSavedManageWorkflowCredentialsKey, NotSavedViewWorkflowRunsKey };

		private const int Everyone = (int)PermissionActions.Everyone;
		private const int DepartmentAdmins = (int)PermissionActions.DepartmentAdminsOnly;

		private static readonly int[] EveryoneFirst = { 3, 0, 1, 2 };
		private static readonly int[] AdminsUpward = { 0, 1, 2 };
		private static readonly int[] AdminsOrGroupAdmins = { 0, 1 };

		private static readonly IReadOnlyList<PermissionScreenEntry> Fixed = new[]
		{
			new PermissionScreenEntry(PermissionTypes.AddPersonnel, AdminsOrGroupAdmins, DepartmentAdmins, rolesOffered: false),
			new PermissionScreenEntry(PermissionTypes.RemovePersonnel, AdminsOrGroupAdmins, DepartmentAdmins, rolesOffered: false),
			new PermissionScreenEntry(PermissionTypes.CreateCall, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.DeleteCall, EveryoneFirst, Everyone, lockToGroupOffered: true),
			new PermissionScreenEntry(PermissionTypes.CloseCall, EveryoneFirst, Everyone, lockToGroupOffered: true),
			new PermissionScreenEntry(PermissionTypes.AddCallData, EveryoneFirst, Everyone, lockToGroupOffered: true),
			new PermissionScreenEntry(PermissionTypes.CreateTraining, AdminsUpward, DepartmentAdmins),
			new PermissionScreenEntry(PermissionTypes.CreateDocument, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.CreateCalendarEntry, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.CreateNote, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.CreateLog, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.DeleteLog, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.CreateShift, AdminsUpward, DepartmentAdmins),
			new PermissionScreenEntry(PermissionTypes.ViewPersonalInfo, EveryoneFirst, Everyone),
			// InventoryAuthorizationService and ClaimsLogic.AddInventoryClaims treat a missing row as department admins.
			new PermissionScreenEntry(PermissionTypes.AdjustInventory, EveryoneFirst, DepartmentAdmins),
			new PermissionScreenEntry(PermissionTypes.CanSeePersonnelLocations, EveryoneFirst, Everyone, lockToGroupOffered: true),
			new PermissionScreenEntry(PermissionTypes.CanSeeUnitLocations, EveryoneFirst, Everyone, lockToGroupOffered: true),
			new PermissionScreenEntry(PermissionTypes.CreateMessage, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.ViewGroupUsers, EveryoneFirst, Everyone, lockToGroupOffered: true),
			new PermissionScreenEntry(PermissionTypes.ViewGroupUnits, EveryoneFirst, Everyone, lockToGroupOffered: true),
			new PermissionScreenEntry(PermissionTypes.ContactView, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.ContactEdit, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.ContactDelete, EveryoneFirst, Everyone),
			// With no row ClaimsLogic gives every member the read claim and admins the write claims, and
			// IsUserAllowed(null) admits everyone to the pages. Saving any offered action narrows reads, so none of
			// them equals the missing row and the screen shows a disabled "not saved" option instead.
			new PermissionScreenEntry(PermissionTypes.CreateWorkflow, AdminsUpward, NotSavedValue, notSavedLabelKey: NotSavedCreateWorkflowKey),
			new PermissionScreenEntry(PermissionTypes.ManageWorkflowCredentials, AdminsUpward, NotSavedValue, notSavedLabelKey: NotSavedManageWorkflowCredentialsKey),
			new PermissionScreenEntry(PermissionTypes.ViewWorkflowRuns, new[] { 0, 1, 2, 3 }, NotSavedValue, notSavedLabelKey: NotSavedViewWorkflowRunsKey),
			new PermissionScreenEntry(PermissionTypes.UseCalendarSync, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.DispatchAppLogin, EveryoneFirst, Everyone),
			new PermissionScreenEntry(PermissionTypes.CommandAppLogin, EveryoneFirst, Everyone),
			// Advanced Data Protection: only the two values a runtime check reads. View Protected Call Data gates SMS/voice
			// PIN release (AdpReleaseService); egress never offers Everyone (ProtectedWorkflowService). Manage (31), the
			// edit/personnel/contact/operational/export values (33-37) and break-glass (39) are not on this screen: nothing
			// enforces them, so a stored row would promise a restriction that does not exist. Leaving them out of this
			// catalog also makes SetPermission refuse them.
			Adp(PermissionTypes.ViewProtectedCallData, includeEveryone: true),
			Adp(PermissionTypes.ConfigureProtectedDataEgress, includeEveryone: false)
		};

		/// <summary>The Records-family catalogs in the order the screen renders them.</summary>
		public static readonly IReadOnlyList<IReadOnlyList<RecordPermissionDescriptor>> RecordCatalogs = new[]
		{
			RecordPermissionCatalog.All, ChecklistPermissionCatalog.All, WorkOrderPermissionCatalog.All, InventoryPermissionCatalog.All,
			InvoicingPermissionCatalog.All, CertificationPermissionCatalog.All, DeploymentPermissionCatalog.All, WorkforcePermissionCatalog.All
		};

		public static readonly IReadOnlyList<PermissionScreenEntry> All = Fixed
			.Concat(RecordCatalogs.SelectMany(catalog => catalog).Where(descriptor => descriptor.ShownOnSecurityScreen).Select(Record))
			.ToList();

		private static readonly Dictionary<int, PermissionScreenEntry> ByType = All.ToDictionary(e => (int)e.Type);

		/// <summary>The screen row for a type, or null when the Permissions screen does not offer that type.</summary>
		public static PermissionScreenEntry Get(int type) => ByType.TryGetValue(type, out var entry) ? entry : null;

		public static PermissionScreenEntry Get(PermissionTypes type) => Get((int)type);

		/// <summary>
		/// The row the screen is showing for a type: the department's own row or, for Transfer and Issue Inventory
		/// with no row of their own, the Adjust Inventory row InventoryAuthorizationService falls back to.
		/// </summary>
		public static Permission EffectiveRow(IEnumerable<Permission> permissions, PermissionTypes type)
		{
			var rows = (permissions ?? Enumerable.Empty<Permission>()).Where(p => p != null).ToList();
			var row = rows.FirstOrDefault(p => p.PermissionType == (int)type);
			if (row == null && type is PermissionTypes.TransferInventory or PermissionTypes.IssueInventory)
				row = rows.FirstOrDefault(p => p.PermissionType == (int)PermissionTypes.AdjustInventory);

			return row;
		}

		/// <summary>The action the dropdown shows: the effective row's action, else the entry's no-row value.</summary>
		public static int CurrentValue(PermissionScreenEntry entry, Permission effectiveRow) => effectiveRow?.Action ?? entry.NoRowValue;

		/// <summary>The group lock the box shows: the effective row's lock, else the runtime's no-row lock. Always false without a box.</summary>
		public static bool CurrentLock(PermissionScreenEntry entry, Permission effectiveRow) =>
			entry.LockToGroupOffered && (effectiveRow?.LockToGroup ?? entry.NoRowLockToGroup);

		/// <summary>
		/// Whether the dropdown lists <paramref name="action"/>: an offered value, or the value already saved (a stored
		/// value outside the offer is still listed so the dropdown never misrepresents what is saved, and re-selecting it
		/// widens nothing). The disabled "not saved" option is never a valid write.
		/// </summary>
		public static bool IsListed(PermissionScreenEntry entry, int action, Permission effectiveRow) =>
			entry != null && action != NotSavedValue && (entry.Actions.Contains(action) || effectiveRow != null && effectiveRow.Action == action);

		/// <summary>
		/// The dropdown items: a disabled "not saved" option first when no row exists and none of the offered actions
		/// matches the missing row, then the offered actions, plus a saved value the entry would not offer.
		/// </summary>
		public static List<SelectListItem> Options(PermissionScreenEntry entry, Permission effectiveRow, PermissionOptionLabels labels, string notSavedLabel = null)
		{
			labels ??= PermissionOptionLabels.English;
			var selected = CurrentValue(entry, effectiveRow);
			var items = new List<SelectListItem>();

			if (selected == NotSavedValue)
				items.Add(new SelectListItem { Value = NotSavedValue.ToString(), Text = notSavedLabel ?? entry.NotSavedLabelKey, Disabled = true, Selected = true });

			var values = entry.Actions.ToList();
			if (selected != NotSavedValue && !values.Contains(selected) && LabelFor(selected, labels) != null)
				values.Add(selected);

			foreach (var value in values)
				items.Add(new SelectListItem { Value = value.ToString(), Text = LabelFor(value, labels), Selected = value == selected });

			return items;
		}

		public static string LabelFor(int action, PermissionOptionLabels labels)
		{
			switch ((PermissionActions)action)
			{
				case PermissionActions.Everyone: return labels.Everyone;
				case PermissionActions.DepartmentAdminsOnly: return labels.DepartmentAdmins;
				case PermissionActions.DepartmentAndGroupAdmins: return labels.DepartmentAndGroupAdmins;
				case PermissionActions.DepartmentAdminsAndSelectRoles: return labels.DepartmentAdminsAndSelectRoles;
				case PermissionActions.DepartmentAndGroupAdminsAndSelectRoles: return labels.DepartmentGroupAdminsAndSelectRoles;
				default: return null;
			}
		}

		private static PermissionScreenEntry Adp(PermissionTypes type, bool includeEveryone) =>
			new PermissionScreenEntry(type, includeEveryone ? EveryoneFirst : AdminsUpward, (int)AdpPermissionDefaults.For(type));

		private static PermissionScreenEntry Record(RecordPermissionDescriptor descriptor) =>
			new PermissionScreenEntry(descriptor.Type,
				descriptor.EveryoneOffered ? new[] { 3, 0, 1, 2, 4 } : new[] { 0, 1, 2, 4 },
				(int)descriptor.NoRowDefault,
				lockToGroupOffered: descriptor.LockToGroupMeaningful,
				noRowLockToGroup: descriptor.NoRowLockToGroup);
	}
}
