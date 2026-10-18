using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 48 (Identifier Allocation Registry section 4A): Department Import maintenance — expiry, retention purge, reminders, helpdesk outbox.</summary>
	public sealed class DepartmentImportMaintenanceCommand : IQuidjiboCommand
	{
		public DepartmentImportMaintenanceCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
