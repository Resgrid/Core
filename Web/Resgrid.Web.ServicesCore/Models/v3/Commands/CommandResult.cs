using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Commands
{
	public class CommandResult
	{
		public int Id { get; set; }

		public int? CallTypeId { get; set; }

		public bool Timer { get; set; }

		public int TimerMinutes { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public List<CommandAssignmentResult> Assignments { get; set; }
	}
}
