using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Dispatch
{
	public class TopActiveCallsView
	{
		public List<Call> Calls { get; set; }
		public Department Department { get; set; }
	}
}
