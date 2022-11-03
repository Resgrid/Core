using System;

namespace Resgrid.Model
{
	public class EventingMessage
	{
		public Guid Id { get; set; }
	
		public int Type { get; set; }

		public DateTime TimeStamp { get; set; }

		public int DepartmentId { get; set; }

		public string ItemId { get; set; }

		public string Payload { get;set;}
	}
}
