using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 57 (Identifier Allocation Registry §4C, Incident Back Office): the day's cost transactions, accrual snapshot, projection and cost-share apportionment.</summary>
	public sealed class IncidentDailyCostCommand : IQuidjiboCommand
	{
		public IncidentDailyCostCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
