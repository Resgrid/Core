using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 89: the department knowledge index (K1) - reindex queue and nightly repair pass.</summary>
	public class KnowledgeIndexCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public KnowledgeIndexCommand(int id)
		{
			Id = id;
		}
	}
}
