using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 98: LMS standards (SCORM / cmi5 package unpacking, xAPI outbox delivery, expired launch and AGS token purge).</summary>
	public class LmsStandardsCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public LmsStandardsCommand(int id)
		{
			Id = id;
		}
	}
}
