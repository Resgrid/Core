using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 43 (registry section 3.3, RMS-3): retention, legal hold and attachment purge, plus the Pending-attachment rescan.</summary>
	public sealed class RmsRetentionAndPurgeCommand : IQuidjiboCommand
	{
		public RmsRetentionAndPurgeCommand(int id)
		{
			Id = id;
		}

		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
