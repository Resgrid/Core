using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 100 (round 10 A3): protocol and course sets over Department Links, LMS sponsorship and countersignature link checks.</summary>
	public class ContentSetsCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public ContentSetsCommand(int id)
		{
			Id = id;
		}
	}
}
