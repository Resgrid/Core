using Resgrid.Model;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.WebCore.Areas.User.Models.Voice
{
	public class NewAudioStreamModel
	{
		public string Id { get; set; }
		public string Message { get; set; }

		[Required]
		public string Name { get; set; }

		[Required]
		public string Url { get; set; }
	}
}
