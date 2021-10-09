using Resgrid.Model;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.WebCore.Areas.User.Models.Voice
{
	public class NewChannelModel
	{
		public string Message { get; set; }

		[Required]
		public string ChannelName { get; set; }
	}
}
