using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Web.Areas.User.Models
{
	public class ActionLogModel: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public List<ActionLog> ActionLogs { get; set; }

		[RegularExpression("^(true|True)$", ErrorMessage = "Confirm required")]
		[DisplayName("Confirm delete all Action Logs")]
		[Required]
		public bool ConfirmClearAll { get; set; }
	}
}