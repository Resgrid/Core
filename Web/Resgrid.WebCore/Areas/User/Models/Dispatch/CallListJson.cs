using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Resgrid.Web.Areas.User.Models.Dispatch
{
	public class CallListJson
	{
		public int CallId { get; set; }
		public string Number { get; set; }
		public string Priority { get; set; }
		public string Color { get; set; }
		public string Name { get; set; }
		public string State { get; set; }
		public string StateColor { get; set; }
		public string Address { get; set; }
		public string Timestamp { get; set; }
		public bool CanDeleteCall { get; set; }
		public bool CanCloseCall { get; set; }
		public bool CanUpdateCall { get; set; }
	}
}
