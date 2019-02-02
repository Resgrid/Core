using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class ShiftsIndexModel
	{
		public List<Shift> Shifts { get; set; }
		public bool IsUserAdminOrGroupAdmin { get; set; }
	}
}