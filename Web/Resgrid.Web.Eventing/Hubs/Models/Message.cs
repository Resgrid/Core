using System;

namespace Resgrid.Web.Eventing.Hubs.Models
{
	public class Message
	{
		public int DepartmentId { get; set; }
		public DateTime Timestamp { get; set; }
		public string Name { get; set; }
		public string Body { get; set; }
	}
}
