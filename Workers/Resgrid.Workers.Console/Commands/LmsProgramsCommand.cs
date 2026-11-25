using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 94: LMS programs (scheduled reports, certification cycle auto-renew and alerts, building-familiarization credit).</summary>
	public class LmsProgramsCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public LmsProgramsCommand(int id)
		{
			Id = id;
		}
	}
}
