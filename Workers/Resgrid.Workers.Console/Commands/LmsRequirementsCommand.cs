using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 93: Training T3 learning requirements (due dates, at-risk and overdue notices, onboarding, enforcement, paths).</summary>
	public class LmsRequirementsCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public LmsRequirementsCommand(int id)
		{
			Id = id;
		}
	}
}
