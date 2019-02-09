using Quidjibo.Commands;
using System;
using System.Collections.Generic;

namespace Resgrid.Workers.Console.Commands
{
	public class GenericCommand : IQuidjiboCommand
	{
		public int Id { get; }

		public GenericCommand(int id)
		{
			Id = id;
		}

		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
