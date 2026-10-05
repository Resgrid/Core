using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Security
{
	/// <summary>
	/// One Records row on the Permissions screen (PermissionTypes 50-67). Rows are generated from
	/// <see cref="RecordPermissionCatalog"/> so the screen, <c>ClaimsLogic.AddRecordClaims</c> and the
	/// activation-time row migration share a single set of no-row defaults.
	/// </summary>
	public class RecordsPermissionRow
	{
		public PermissionTypes Type { get; set; }
		public int PermissionType => (int)Type;
		public string Name => Type.ToString();

		/// <summary>DOM id of the action dropdown. The lock checkbox and the roles span/div/select derive from it.</summary>
		public string ElementId => "Record_" + Name;
		public string LockElementId => "Lock_" + ElementId;
		public string LabelKey => "PermRecords" + Name + "Label";
		public string NoteKey => "PermRecords" + Name + "Note";

		/// <summary>The stored action, or the catalog no-row default when the department has no row.</summary>
		public int Value { get; set; }
		public bool HasRow { get; set; }
		public bool LockToGroup { get; set; }
		public bool ShowLockToGroup { get; set; }
		public SelectList Options { get; set; }
	}

	public static class RecordsPermissionRows
	{
		public const string EveryoneValue = "3";
		public const string DepartmentAndGroupAdminsAndSelectRolesValue = "4";

		public static List<RecordsPermissionRow> Build(IEnumerable<Permission> permissions, IEnumerable<RecordPermissionDescriptor> descriptors = null, PermissionOptionLabels labels = null)
		{
			var existing = (permissions ?? Enumerable.Empty<Permission>()).Where(p => p != null).ToList();
			var rows = new List<RecordsPermissionRow>();

			foreach (var descriptor in (descriptors ?? RecordPermissionCatalog.All).Where(d => d.ShownOnSecurityScreen))
			{
				var row = PermissionScreenCatalog.EffectiveRow(existing, descriptor.Type);
				var value = row != null ? row.Action : (int)descriptor.NoRowDefault;

				rows.Add(new RecordsPermissionRow
				{
					Type = descriptor.Type,
					Value = value,
					HasRow = row != null,
					LockToGroup = row != null ? row.LockToGroup : descriptor.NoRowLockToGroup,
					ShowLockToGroup = descriptor.LockToGroupMeaningful,
					Options = BuildOptions(descriptor.EveryoneOffered, value, labels)
				});
			}

			return rows;
		}

		/// <summary>
		/// The action dropdown. Value 4 (department and group admins plus selected roles) is offered on every
		/// Records row; "Everyone" only where the catalog allows it. A stored value that the catalog would not
		/// offer is still listed so the dropdown never misrepresents what is saved. Option text comes from
		/// <paramref name="labels"/> (the localized Security resources), falling back to English.
		/// </summary>
		public static SelectList BuildOptions(bool includeEveryone, int selected, PermissionOptionLabels labels = null)
		{
			labels ??= PermissionOptionLabels.English;
			var options = new List<SelectListItem>();
			var selectedValue = selected.ToString();

			if (includeEveryone || selectedValue == EveryoneValue)
				options.Add(new SelectListItem { Value = EveryoneValue, Text = labels.Everyone });

			options.Add(new SelectListItem { Value = "0", Text = labels.DepartmentAdmins });
			options.Add(new SelectListItem { Value = "1", Text = labels.DepartmentAndGroupAdmins });
			options.Add(new SelectListItem { Value = "2", Text = labels.DepartmentAdminsAndSelectRoles });
			options.Add(new SelectListItem { Value = DepartmentAndGroupAdminsAndSelectRolesValue, Text = labels.DepartmentGroupAdminsAndSelectRoles });

			return new SelectList(options, "Value", "Text", selectedValue);
		}
	}
}
