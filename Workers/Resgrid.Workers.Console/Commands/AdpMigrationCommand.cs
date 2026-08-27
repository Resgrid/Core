using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	public sealed class AdpMigrationCommand : IQuidjiboCommand
	{
		public AdpMigrationCommand(int id)
		{
			Id = id;
		}

		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
