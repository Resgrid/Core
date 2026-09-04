using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 44 (registry section 3.3, Unified Search allocation absorbed by RMS-1): records search index maintenance sweep (RMS plan section 5.10).</summary>
	public sealed class RecordsSearchIndexCommand : IQuidjiboCommand
	{
		public RecordsSearchIndexCommand(int id)
		{
			Id = id;
		}

		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
