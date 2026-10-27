using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 55 (Identifier Allocation Registry §4C, Incident Back Office): the evaluated LI-B/LI-C/LI-D triggers through the edge latch.</summary>
	public sealed class IncidentAlertEvaluationCommand : IQuidjiboCommand
	{
		public IncidentAlertEvaluationCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
