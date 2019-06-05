namespace Resgrid.Web.Services.Models
{
	public class EmailModel
	{
		public string headers { get; set; }
		public string dkim { get; set; }
		public string to { get; set; }
		public string html { get; set; }
		public string from { get; set; }
		public string text { get; set; }
		public string sender_ip { get; set; }
		public string SPF { get; set; }
		public string attachments { get; set; }
		public string subject { get; set; }
		public string envelope { get; set; }
		public string charsets { get; set; }
	}
}
