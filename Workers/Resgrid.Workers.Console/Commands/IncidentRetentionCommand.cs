using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 58 (Identifier Allocation Registry §4C, Incident Back Office): snapshot and metric retention after close (setting 97).</summary>
	public sealed class IncidentRetentionCommand : IQuidjiboCommand
	{
		public IncidentRetentionCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
