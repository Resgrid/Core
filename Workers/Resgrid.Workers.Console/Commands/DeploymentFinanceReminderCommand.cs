using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 32 (Identifier Allocation Registry, Workforce &amp; Business Operations plan C7): deployment finance reminder.</summary>
	public sealed class DeploymentFinanceReminderCommand : IQuidjiboCommand
	{
		public DeploymentFinanceReminderCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
