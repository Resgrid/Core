using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Commands
{
	public class CommandAssignmentResult
	{
		public int RoleId { get; set; }

		public int CommandId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public int MinUnitPersonnel { get; set; }

		public int MaxUnitPersonnel { get; set; }

		public int MaxUnits { get; set; }

		public int MinTimeInRole { get; set; }

		public int MaxTimeInRole { get; set; }

		public bool ForceRequirements { get; set; }

		public List<AssignmentUnitTypeResult> RequiredUnitTypes { get; set; }

		public List<AssignmentCertResult> RequiredCerts { get; set; }

		public List<AssignmentPersonnelResult> RequiredRoles { get; set; }
	}
}
