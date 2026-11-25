using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 95: LMS class sessions (reminders, waitlist promotion, roster retention purge).</summary>
	public class LmsClassesCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public LmsClassesCommand(int id)
		{
			Id = id;
		}
	}
}
