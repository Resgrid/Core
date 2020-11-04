using System.ComponentModel.DataAnnotations;

namespace Resgrid.WebCore.Areas.User.Models.Files
{
	public class UploadFileView
	{
		[Required]
		public string Name { get; set; }
		public string Message { get; set; }
		public int Type { get; set; }
		public string ResourceId { get; set; }
	}
}
