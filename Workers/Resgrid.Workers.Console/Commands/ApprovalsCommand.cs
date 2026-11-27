using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 97: e-Signature S4 approval chains (step announcements with delegation, escalation, closed-request sync).</summary>
	public class ApprovalsCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public ApprovalsCommand(int id)
		{
			Id = id;
		}
	}
}
