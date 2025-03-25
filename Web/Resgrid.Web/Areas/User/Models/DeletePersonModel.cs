using System;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models
{
	public class DeletePersonModel: BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }
		public string UserId { get; set; }

		[Required]
		public bool AreYouSure { get; set; }
	}
}
