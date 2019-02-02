using System;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Dispatch
{
	public class CallJson
	{
		public int CallId { get; set; }
		public string Priority { get; set; }
		public CallPriority PriorityEnum { get; set; }
		public string Name { get; set; }
		public string State { get; set; }
		public string Nature { get; set; }
		public string Address { get; set; }
		public DateTime DispatchTime { get; set; }
	}
}