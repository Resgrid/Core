using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Web.Areas.User.Models
{
	public class InvitesView : BaseUserModel
	{
		public Department Department { get; set; }
		public IdentityUser User { get; set; }

		public List<Invite> Invites { get; set; }

		[Required]
		[MinLength(5)]
		[Display(Name = "Email Addresses")]
		public string EmailAddresses { get; set; }
	}
}