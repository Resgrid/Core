using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 29 (Identifier Allocation Registry, Workforce &amp; Business Operations plan B5): invoice maintenance.</summary>
	public sealed class InvoiceMaintenanceCommand : IQuidjiboCommand
	{
		public InvoiceMaintenanceCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
