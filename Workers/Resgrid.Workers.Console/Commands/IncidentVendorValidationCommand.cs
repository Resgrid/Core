using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 53 (Identifier Allocation Registry §4C, Incident Back Office): vendor compliance expiry (trigger 134) and expired documents.</summary>
	public sealed class IncidentVendorValidationCommand : IQuidjiboCommand
	{
		public IncidentVendorValidationCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
