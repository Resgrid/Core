using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Account
{
	public class DeleteAccountModel : BaseUserModel
	{
		[Required]
		public bool AreYouSure { get; set; }

		public bool IsDepartmentOwner { get; set; }
	}
}
