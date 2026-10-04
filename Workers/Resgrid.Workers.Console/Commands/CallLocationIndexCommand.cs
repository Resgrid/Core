using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 73: call location index backfill and data protection suppression (call location history, M0259).</summary>
	public sealed class CallLocationIndexCommand : IQuidjiboCommand
	{
		public CallLocationIndexCommand(int id)
		{
			Id = id;
		}

		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
