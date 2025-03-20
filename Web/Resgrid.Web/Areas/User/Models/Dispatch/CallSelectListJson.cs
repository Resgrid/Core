
using System.Collections.Generic;

namespace Resgrid.WebCore.Areas.User.Models.Dispatch
{
	public class CallSelectListJson
	{
		public List<CallSelectListJsonResult> results { get; set; }
	}


	public class CallSelectListJsonResult
	{
		public int id { get; set; }
		public string text { get; set; }
	}
}
