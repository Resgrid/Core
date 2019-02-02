using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Resgrid.Web.Areas.User.Models.Dispatch
{
	public class CallPersonnelForJson
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public bool Dispatched { get; set; }
		public string CheckValue { get; set; }
		public string Group { get; set; }
		public List<string> Roles { get; set; } 
	}
}