using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.UnitRoles
{
	/// <summary>
	/// Object inputs for setting a users Status/Action. If this object is used in an operation that sets
	/// a status for the current user the UserId value in this object will be ignored.
	/// </summary>
	public class SetUnitRolesInput
	{
		/// <summary>
		/// UnitId of the apparatus that the roles are being set for
		/// </summary>
		[Required]
		public string UnitId { get; set; }

		/// <summary>
		/// The accountability roles filed for this event
		/// </summary>
		public List<SetUnitRolesRoleInput> Roles { get; set; }
	}

	/// <summary>
	/// Role filled by a User on a Unit
	/// </summary>
	public class SetUnitRolesRoleInput
	{
		/// <summary>
		/// UserId of the user filling the role
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// RoleId of the role being filled
		/// </summary>
		public string RoleId { get; set; }

		/// <summary>
		///  The name of the Role
		/// </summary>
		public string Name { get; set; }
	}
}
