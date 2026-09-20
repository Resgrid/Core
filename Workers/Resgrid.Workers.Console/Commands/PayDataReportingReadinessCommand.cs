using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 49 (Identifier Allocation Registry, Workforce &amp; Business Operations plan E5): California pay data readiness sweep and export artifact purge.</summary>
	public sealed class PayDataReportingReadinessCommand : IQuidjiboCommand
	{
		public PayDataReportingReadinessCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
