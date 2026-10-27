using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 75 (Identifier Allocation Registry §4C, Incident Back Office): full rebuild snapshot per open activation; drift is flagged.</summary>
	public sealed class IncidentSnapshotReconciliationCommand : IQuidjiboCommand
	{
		public IncidentSnapshotReconciliationCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
