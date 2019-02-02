using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Connect
{
	public class ArticleResult
	{
		public int Id { get; set; }
		public string Title { get; set; }
		public string Body { get; set; }
		public byte[] SmallImage { get; set; }
		public byte[] LargeImage { get; set; }
		public string Keywords { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime StartOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
	}
}