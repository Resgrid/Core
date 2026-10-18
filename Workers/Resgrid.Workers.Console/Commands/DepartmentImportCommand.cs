using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 47 (Identifier Allocation Registry section 4A): the Department Import run sweep behind the RabbitMQ nudge.</summary>
	public sealed class DepartmentImportCommand : IQuidjiboCommand
	{
		public DepartmentImportCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
