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

		public static List<RecordsPermissionRow> Build(IEnumerable<Permission> permissions)
		{
			var existing = (permissions ?? Enumerable.Empty<Permission>()).Where(p => p != null).ToList();
			var rows = new List<RecordsPermissionRow>();

			foreach (var descriptor in RecordPermissionCatalog.All)
			{
				var row = existing.FirstOrDefault(p => p.PermissionType == (int)descriptor.Type);
				var value = row != null ? row.Action : (int)descriptor.NoRowDefault;

				rows.Add(new RecordsPermissionRow
				{
					Type = descriptor.Type,
					Value = value,
					HasRow = row != null,
					LockToGroup = row != null && row.LockToGroup,
					ShowLockToGroup = descriptor.LockToGroupMeaningful,
					Options = BuildOptions(descriptor.EveryoneOffered, value)
				});
			}

			return rows;
		}

		/// <summary>
		/// The action dropdown. Value 4 (department and group admins plus selected roles) is offered on every
		/// Records row; "Everyone" only where the catalog allows it. A stored value that the catalog would not
		/// offer is still listed so the dropdown never misrepresents what is saved.
		/// </summary>
		public static SelectList BuildOptions(bool includeEveryone, int selected)
		{
			var options = new List<SelectListItem>();
			var selectedValue = selected.ToString();

			if (includeEveryone || selectedValue == EveryoneValue)
				options.Add(new SelectListItem { Value = EveryoneValue, Text = "Everyone" });

			options.Add(new SelectListItem { Value = "0", Text = "Department Admins" });
			options.Add(new SelectListItem { Value = "1", Text = "Department and Group Admins" });
			options.Add(new SelectListItem { Value = "2", Text = "Department Admins and Select Roles" });
			options.Add(new SelectListItem { Value = DepartmentAndGroupAdminsAndSelectRolesValue, Text = "Department, Group Admins and Select Roles" });

			return new SelectList(options, "Value", "Text", selectedValue);
		}
	}
}
