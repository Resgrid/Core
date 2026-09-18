using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 70 (registry §4F, Unified Search): global search index maintenance sweep (plan R4 Phase 2). Same process as 44, the sole IndexWriter owner.</summary>
	public sealed class SearchIndexCommand : IQuidjiboCommand
	{
		public SearchIndexCommand(int id)
		{
			Id = id;
		}

		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
