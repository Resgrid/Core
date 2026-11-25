using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 85: the training history backfill jobs (Training T1, R-T3).</summary>
	public class TrainingRecordsCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public TrainingRecordsCommand(int id)
		{
			Id = id;
		}
	}
}
