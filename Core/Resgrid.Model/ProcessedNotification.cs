using System;
using System.Collections.Generic;
using Resgrid.Model.Events;

namespace Resgrid.Model
{
	public class ProcessedNotification
	{
		public string MessageId { get; set; }
		public int DepartmentId { get; set; }
		public int ItemId { get; set; }
		public EventTypes Type { get; set; }
		public string Data { get; set; }
		public string Value { get; set; }
		public List<string> Users { get; set; }
		public int PersonnelRoleTargeted { get; set; }
		public string UnitTypeTargeted { get; set; }

		public ProcessedNotification()
		{
			Users = new List<string>();
		}
	}
}