using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Events;

namespace Resgrid.Workers.Framework.Workers.Notification
{
	public class NotificationQueueItem : QueueItem
	{
		public Department Department { get; set; }
		public string DepartmentTextNumber { get; set; }
		public List<DepartmentNotification> NotificationSettings { get; set; }
		public List<ProcessedNotification> Notifications { get; set; }
		public Dictionary<string, UserProfile> Profiles { get; set; } 

		public NotificationQueueItem()
		{
			NotificationSettings = new List<DepartmentNotification>();
			Notifications = new List<ProcessedNotification>();
		}
	}
}