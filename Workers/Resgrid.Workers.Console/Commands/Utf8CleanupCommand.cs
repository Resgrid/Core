using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	public class Utf8CleanupCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public Utf8CleanupCommand(int id)
		{
			Id = id;
		}
	}
}
