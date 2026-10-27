using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 54 (Identifier Allocation Registry §4C, Incident Back Office): capacity holds and lodging blocks past expiry (trigger 202 ahead of it).</summary>
	public sealed class IncidentHoldExpiryCommand : IQuidjiboCommand
	{
		public IncidentHoldExpiryCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
