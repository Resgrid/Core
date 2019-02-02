using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Home
{
	public class ActivityStatsModel
	{
		public string ActivityCount { get; set; }
		public string ActivityNumbers { get; set; }
		public string ActivityChange { get; set; }

		//public string LogsCount { get; set; }
		//public string LogsNumbers { get; set; }
		//public string LogsChange { get; set; }
		
		public string CallsCount { get; set; }
		public string CallsNumbers { get; set; }
		public string CallsChange { get; set; }
		public int SetupScore { get; set; }
	}
}