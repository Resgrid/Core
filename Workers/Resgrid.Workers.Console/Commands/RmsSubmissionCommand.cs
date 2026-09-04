using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 41 (registry section 3.3, RMS-2): NERIS/reporting-destination submission sweep (RMS plan sections 5.3/5.5).</summary>
	public sealed class RmsSubmissionCommand : IQuidjiboCommand
	{
		public RmsSubmissionCommand(int id)
		{
			Id = id;
		}

		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
