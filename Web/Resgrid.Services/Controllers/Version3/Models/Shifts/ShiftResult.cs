using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Shifts
{
	public class ShiftResult
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public string Color { get; set; }
		public int SType { get; set; }
		public int AType { get; set; }
		public bool InShift { get; set; }
		public int PCount { get; set; }
		public int GCount { get; set; }
		public string NextDay { get; set; }
		public int NextDayId { get; set; }
	}
}