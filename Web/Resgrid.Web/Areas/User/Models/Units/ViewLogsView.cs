using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Units
{
	public class ViewLogsView : BaseUserModel
	{
		public Department Department { get; set; }
		public Unit Unit { get; set; }
		public List<UnitLog> Logs { get; set; }

		/// <summary>True when this page is showing ADP placeholders that a grant could reveal.</summary>
		public bool IsProtectedLogs { get; set; }
		/// <summary>True once Records is active for the department: legacy unit logs are read-only (RMS plan section 4.1).</summary>
		public bool LegacyReadOnly { get; set; }

		public bool ConfirmClearAll { get; set; }
		public string Message { get; set; }
		public string OSMKey { get; set; }
		public double CenterLat { get; set; }
		public double CenterLon { get; set; }
	}
}
