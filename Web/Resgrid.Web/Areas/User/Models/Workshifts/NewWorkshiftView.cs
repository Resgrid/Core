using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Workshifts
{
	public class NewWorkshiftView
	{
		public string Message { get; set; }
		public Workshift Shift { get; set; }
		public List<string> UnitsAssigned { get; set; }

		public NewWorkshiftView()
		{
			Shift = new Workshift();
		}
	}
}
