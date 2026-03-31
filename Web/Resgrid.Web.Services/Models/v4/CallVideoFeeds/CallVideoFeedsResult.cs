using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CallVideoFeeds
{
	public class CallVideoFeedsResult : StandardApiResponseV4Base
	{
		public List<CallVideoFeedResultData> Data { get; set; }

		public CallVideoFeedsResult()
		{
			Data = new List<CallVideoFeedResultData>();
		}
	}

	public class CallVideoFeedResultData
	{
		public string CallVideoFeedId { get; set; }
		public string CallId { get; set; }
		public string Name { get; set; }
		public string Url { get; set; }
		public int? FeedType { get; set; }
		public int? FeedFormat { get; set; }
		public string Description { get; set; }
		public int Status { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public string AddedByUserId { get; set; }
		public string AddedOnFormatted { get; set; }
		public DateTime AddedOnUtc { get; set; }
		public int SortOrder { get; set; }
		public string FullName { get; set; }
	}
}
