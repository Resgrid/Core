namespace Resgrid.Model
{
	public class EmailNotification
	{
		public string To { get; set; }
		public string From { get; set; }
		public string Name { get; set; }
		public string Subject { get; set; }
		public string Body { get; set; }
		public byte[] AttachmentData { get; set; }
		public string AttachmentName { get; set; }
	}
}