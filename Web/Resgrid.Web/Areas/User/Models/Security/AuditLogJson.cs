namespace Resgrid.Web.Areas.User.Models.Security
{
	public class AuditLogJson
	{
		public int AuditLogId { get; set; }
		public string Type { get; set; }
		public string Timestamp { get; set; }
		public string Name { get; set; }
		public string Message { get; set; }
	}
}