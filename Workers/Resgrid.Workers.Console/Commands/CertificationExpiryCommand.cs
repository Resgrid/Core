using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 34 (Identifier Allocation Registry, Workforce &amp; Business Operations plan D5): certification expiry sweep.</summary>
	public sealed class CertificationExpiryCommand : IQuidjiboCommand
	{
		public CertificationExpiryCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
