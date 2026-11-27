using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 84: protocol version activation and expiry, review reminders and retention purge.</summary>
	public class ProtocolLifecycleCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public ProtocolLifecycleCommand(int id)
		{
			Id = id;
		}
	}
}
