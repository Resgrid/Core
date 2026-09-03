using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 40 (registry section 3.3): durable catch-up sweep of the DomainEventOutbox.</summary>
	public sealed class DomainEventOutboxDispatchCommand : IQuidjiboCommand
	{
		public DomainEventOutboxDispatchCommand(int id)
		{
			Id = id;
		}

		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
