using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Types
{
	public class EditCallTypeView
	{
		public string Message { get; set; }
		public CallType CallType { get; set; }
		public int CallTypeIcon { get; set; }
	}
}
