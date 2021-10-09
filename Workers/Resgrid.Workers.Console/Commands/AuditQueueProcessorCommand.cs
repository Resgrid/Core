using System;
using System.Collections.Generic;
using Quidjibo.Attributes;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	public class AuditQueueProcessorCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public AuditQueueProcessorCommand(int id)
		{
			Id = id;
		}
	}
}
