using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 96: LMS skills (skill notice digests, task book validity expiry).</summary>
	public class LmsSkillsCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public LmsSkillsCommand(int id)
		{
			Id = id;
		}
	}
}
