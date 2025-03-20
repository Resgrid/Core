using System;

namespace Resgrid.Web.Areas.User.Models.Dispatch
{
	public class CallNoteJson
	{
		public int CallNoteId { get; set; }
		public string UserId { get; set; }
		public string Name { get; set; }
		public string Note { get; set; }
		public string Timestamp { get; set; }
		public string Location { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public bool IsFlagged { get; set; }
	}
}
