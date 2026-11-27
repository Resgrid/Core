using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 91: member wellness WL0 (post-call exposure suggestions, anniversary reminders, retention).</summary>
	public class WellnessCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public WellnessCommand(int id)
		{
			Id = id;
		}
	}
}
