using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models
{
	public class ComposeMessageModel : BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public Message Message { get; set; }
		public bool SendToAll { get; set; }
		public bool SendToMatchOnly { get; set; }
		public MessageTypes MessageType { get; set; }
		public SelectList Types { get; set; }
		public SelectList Shifts { get; set; }
	}
}
