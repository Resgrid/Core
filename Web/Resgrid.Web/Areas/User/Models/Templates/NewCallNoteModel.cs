using System.ComponentModel.DataAnnotations;

namespace Resgrid.WebCore.Areas.User.Models.Templates
{
	public class NewCallNoteModel
	{
		public string Message { get; set; }

		public int Sort { get; set; }

		[Required]
		public string Name { get; set; }

		[Required]
		public string Data { get; set; }
	}
}
