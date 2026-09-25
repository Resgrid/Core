using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 72, registry §4G. Metadata health refresh and opted-in administrative digests.</summary>
	public sealed class AdminAssistMaintenanceCommand(int id) : IQuidjiboCommand
	{
		public int Id { get; } = id;
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
