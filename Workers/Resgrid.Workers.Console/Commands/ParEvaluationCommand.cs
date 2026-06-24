using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Scheduled command that runs the periodic personnel accountability (PAR) sweep.</summary>
	public class ParEvaluationCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public ParEvaluationCommand(int id)
		{
			Id = id;
		}
	}
}
