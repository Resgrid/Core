using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Units
{
	public class ViewLogsView : BaseUserModel
	{
		public Department Department { get; set; }
		public Unit Unit { get; set; }
		public List<UnitLog> Logs { get; set; }

		public bool ConfirmClearAll { get; set; }
		public string Message { get; set; }
		public string OSMKey { get; set; }
		public double CenterLat { get; set; }
		public double CenterLon { get; set; }
	}
}
