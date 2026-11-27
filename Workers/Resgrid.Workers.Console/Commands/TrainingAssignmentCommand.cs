using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 83: dynamic training audiences on membership change, and the one simple repeat.</summary>
	public class TrainingAssignmentCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public TrainingAssignmentCommand(int id)
		{
			Id = id;
		}
	}
}
