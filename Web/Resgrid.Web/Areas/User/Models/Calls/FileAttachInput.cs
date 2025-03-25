using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Calls
{
	public class FileAttachInput
	{
		public int CallId { get; set; }

		[Required]
		public string FriendlyName { get; set; }
	}
}