using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Protocols
{
	public class NewProtocolModel
	{
		public string Message { get; set; }
		public DispatchProtocol Protocol { get; set; }
		public SelectList CallPriorities { get; set; }
		public SelectList CallTypes { get; set; }
		public SelectList TriggerTypes { get; set; }
		public ProtocolTriggerTypes TriggerTypesEnum { get; set; }
	}
}
