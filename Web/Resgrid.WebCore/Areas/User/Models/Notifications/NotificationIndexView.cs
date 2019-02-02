using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Notifications
{
	public class NotificationIndexView
	{
		public string Message { get; set; }
		public List<DepartmentNotification> Notifications { get; set; }
		public Dictionary<int, string> NotifyUsers { get; set; }
		public Dictionary<int, string> NotifyData { get; set; }
		public List<CustomState> CustomStates { get; set; } 

		public NotificationIndexView()
		{
			NotifyUsers = new Dictionary<int, string>();
			NotifyData = new Dictionary<int, string>();
		}
	}
}