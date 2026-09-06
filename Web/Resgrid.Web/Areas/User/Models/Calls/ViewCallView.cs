using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;
using System;

namespace Resgrid.Web.Areas.User.Models.Calls
{
	public class ViewCallView: BaseUserModel
	{
		public string UdfReadOnlyHtml { get; set; }
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public Call Call { get; set; }
		public string Message { get; set; }
		public List<DepartmentGroup> Groups { get; set; }
		public List<IdentityUser> UnGroupedUsers { get; set; }
		public CallPriority CallPriority { get; set; }
		public SelectList CallPriorities { get; set; }
		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public List<UnitState> UnitStates { get; set; }
		public List<ActionLog> ActionLogs { get; set; }
		public List<UserGroupRole> UserGroupRoles { get; set; }
		public List<Unit> Units { get; set; }
		public List<DepartmentGroup> Stations { get; set; }
		public List<DispatchProtocol> Protocols { get; set; }
		public List<CallReference> ChildCalls { get; set; }
		public List<Contact> Contacts { get; set; }
		public List<CallVideoFeed> VideoFeeds { get; set; } = new List<CallVideoFeed>();
		public string DestinationName { get; set; }
		public string DestinationAddress { get; set; }
		public string DestinationTypeName { get; set; }

		/// <summary>RMS: false hides the Incident Report control, which would otherwise 404 on Start.</summary>
		public bool RecordsUsable { get; set; }

		/// <summary>ADP: true when this call carries protected fields rendered as REDACTED (plan 7.2).</summary>
		public bool IsProtectedCall { get; set; }
		public string ProtectedReason { get; set; }

		/// <summary>
		/// True when the department enforces Protected Data over Records: starting an incident report from this
		/// call seals the captured location and narrative, so the start form needs a grant (RMS plan section 5.9.3).
		/// </summary>
		public bool ProtectionEnforced { get; set; }
		public List<string> RedactedFields { get; set; } = new List<string>();

		public string IsMapTabActive()
		{
			if (!String.IsNullOrEmpty(Call.Address) || !String.IsNullOrEmpty(Call.GeoLocationData))
				return "active";

			return "";
		}

		public string IsDispatchTabActive()
		{
			if (String.IsNullOrEmpty(Call.Address) && String.IsNullOrEmpty(Call.GeoLocationData))
				return "active";

			return "";
		}
	}
}
