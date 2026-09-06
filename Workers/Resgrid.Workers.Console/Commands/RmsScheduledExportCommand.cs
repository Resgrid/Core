using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 45 (registry section 3.3, RMS-3e): renders every department export template whose schedule is due and raises RecordExportScheduled (160).</summary>
	public sealed class RmsScheduledExportCommand : IQuidjiboCommand
	{
		public RmsScheduledExportCommand(int id)
		{
			Id = id;
		}

		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
