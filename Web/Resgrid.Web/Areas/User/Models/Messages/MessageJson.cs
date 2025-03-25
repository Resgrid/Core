using System;

namespace Resgrid.Web.Areas.User.Models.Messages
{
	public class MessageJson
	{
		public int MessageId { get; set; }
		public string Subject { get; set; }
		public bool SystemGenerated { get; set; }
		public string Body { get; set; }
		public string SentBy { get; set; }
		public string SentOn { get; set; }
		public bool IsDeleted { get; set; }
		public DateTime? ReadOn { get; set; }
		public int Type { get; set; }
		public DateTime? ExpireOn { get; set; }
		public bool Read { get; set; }
	}
}