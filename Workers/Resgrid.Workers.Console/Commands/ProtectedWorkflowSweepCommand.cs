using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 71 (ADP Protected Workflows): daily release expiry, offboarding revocation and expiry-notice sweep.</summary>
	public sealed class ProtectedWorkflowSweepCommand : IQuidjiboCommand
	{
		public ProtectedWorkflowSweepCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
