using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 42 (registry section 3.3, RMS-3): the bounded RecordOverdue due-state evaluation (RMS plan section 4.7).</summary>
	public sealed class RmsDueStateEvaluationCommand : IQuidjiboCommand
	{
		public RmsDueStateEvaluationCommand(int id)
		{
			Id = id;
		}

		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
