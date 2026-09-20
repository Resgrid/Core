using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 33 (Identifier Allocation Registry, Workforce &amp; Business Operations plan C7): contract and compliance document expiry.</summary>
	public sealed class ComplianceExpiryCommand : IQuidjiboCommand
	{
		public ComplianceExpiryCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
