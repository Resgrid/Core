using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 81: Enhanced AI destination advice reviews.</summary>
	public class AiDestinationAdviceCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public AiDestinationAdviceCommand(int id)
		{
			Id = id;
		}
	}
}
