using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 80: intake retention purges and the dedicated number lifecycle (call-intake-and-ai-destinations-plan.md §3).</summary>
	public class IntakeMaintenanceCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public IntakeMaintenanceCommand(int id)
		{
			Id = id;
		}
	}
}
