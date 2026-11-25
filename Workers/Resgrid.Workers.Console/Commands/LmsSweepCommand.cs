using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 88: LMS sweeps (Academy seed, in-progress lesson reminders, lapse-rule housekeeping).</summary>
	public class LmsSweepCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public LmsSweepCommand(int id)
		{
			Id = id;
		}
	}
}
