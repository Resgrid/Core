namespace Resgrid.Web.Areas.User.Models.Logs
{
	public class LogForListJson
	{
		public int LogId { get; set; }
		public string Type { get; set; }
		public string Group { get; set; }
		public string LoggedOn { get; set; }
		public string LoggedBy { get; set; }
		public bool CanDelete { get; set; }
	}
}