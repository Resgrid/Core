using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Units
{
	public class UnitDetailResult
	{
		public int Id { get; set; }

		public string Name { get; set; }

		public string Type { get; set; }

		public int GroupId { get; set; }

		public string VIN { get; set; }

		public string PlateNumber { get; set; }

		public bool Offroad { get; set; }

		public bool SpecialPermit { get; set; }

		public int StatusId { get; set; }

		public List<UnitDetailRoleResult> Roles { get; set; }
	}
}
