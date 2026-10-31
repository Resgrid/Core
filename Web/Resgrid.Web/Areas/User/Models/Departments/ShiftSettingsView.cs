using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	public class ShiftSettingsView
	{
		public bool AllowSignupsForMultipleShiftGroups { get; set; }

		/// <summary>Group admins may pick the applicant for (and approve) trades in their groups.</summary>
		public bool TradeApproversGroupAdmins { get; set; }

		/// <summary>The person trading out picks who takes their slot.</summary>
		public bool TradeApproversRequester { get; set; }

		/// <summary>Members of these roles may decide any trade.</summary>
		public List<int> TradeApproverRoleIds { get; set; } = new List<int>();

		public List<PersonnelRole> Roles { get; set; } = new List<PersonnelRole>();

		public bool? SaveSuccess { get; set; }
		public string Message { get; set; }

	}
}
