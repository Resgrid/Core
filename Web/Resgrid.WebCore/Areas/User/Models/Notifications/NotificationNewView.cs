using System.Collections.Generic;
using System.Web.Mvc;
using Resgrid.Model;
using Resgrid.Model.Events;

namespace Resgrid.Web.Areas.User.Models.Notifications
{
	public class NotificationNewView
	{
		public string Message { get; set; }
		public DepartmentNotification Notification { get; set; }

		public List<PersonnelRole> PersonnelRoles { get; set; }

		//public SelectList Roles
		//{
		//	get
		//	{
		//		if (PersonnelRoles != null)
		//			return new SelectList(PersonnelRoles, "PersonnelRoleId", "Name");
		//		else
		//			return null;
		//	}
		//}

		public EventTypes Type { get; set; }
		public int SelectedRole { get; set; }


		public List<UnitType> UnitsTypes { get; set; }

		//public SelectList UnitTypes
		//{
		//	get
		//	{
		//		if (PersonnelRoles != null)
		//			return new SelectList(UnitsTypes, "UnitTypeId", "Type");
		//		else
		//			return null;
		//	}
		//}

		public int SelectedUnitType { get; set; }
	}
}