using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 99: Training T5 data exit (build queued department exports, remove expired ones).</summary>
	public class LmsDataExportCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public LmsDataExportCommand(int id)
		{
			Id = id;
		}
	}
}
