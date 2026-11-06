using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Scheduled command (worker 78) that re-notifies dispatched personnel and units who have not acknowledged a call.</summary>
	public class DispatchRenotifyCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public DispatchRenotifyCommand(int id)
		{
			Id = id;
		}
	}
}
