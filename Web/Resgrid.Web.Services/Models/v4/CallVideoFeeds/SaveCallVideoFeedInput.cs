using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.CallVideoFeeds
{
	public class SaveCallVideoFeedInput
	{
		[Required]
		public string CallId { get; set; }

		[Required]
		public string Name { get; set; }

		[Required]
		public string Url { get; set; }

		[Range(0, int.MaxValue)]
		public int? FeedType { get; set; }

		[Range(0, int.MaxValue)]
		public int? FeedFormat { get; set; }

		public string Description { get; set; }

		public string Latitude { get; set; }

		public string Longitude { get; set; }

		[Range(0, int.MaxValue)]
		public int SortOrder { get; set; }
	}
}
