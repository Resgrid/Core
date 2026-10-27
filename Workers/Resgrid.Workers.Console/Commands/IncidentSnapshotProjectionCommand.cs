using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 74 (Identifier Allocation Registry §4C, Incident Back Office): incremental operational snapshot per open activation; trigger 120 when the projection lags.</summary>
	public sealed class IncidentSnapshotProjectionCommand : IQuidjiboCommand
	{
		public IncidentSnapshotProjectionCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
