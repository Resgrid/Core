using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 46 (registry section 3.3, RMS-1C connectors): reads every enabled external order connector whose poll interval has elapsed.</summary>
	public sealed class RmsConnectorPollCommand : IQuidjiboCommand
	{
		public RmsConnectorPollCommand(int id)
		{
			Id = id;
		}

		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
