using System;

namespace Resgrid.Web.Areas.User.Models.Connect
{
	public class PostJson
	{
		public int Id { get; set; }
		public string Title { get; set; }
		public string Body { get; set; }
		public byte[] SmallImage { get; set; }
		public byte[] LargeImage { get; set; }
		public string CreatedOn { get; set; }
		public string ExpiresOn { get; set; }
		public string CreatedBy { get; set; }
	}
}