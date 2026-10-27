using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 52 (Identifier Allocation Registry §4C, Incident Back Office): meal demand versions for the next three days of meal plans.</summary>
	public sealed class IncidentDemandForecastCommand : IQuidjiboCommand
	{
		public IncidentDemandForecastCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
