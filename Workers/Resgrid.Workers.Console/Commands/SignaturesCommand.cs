using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 90: e-Signature S1/S2 (retry sealing, reconcile envelopes, expire requests, re-timestamp archives).</summary>
	public class SignaturesCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public SignaturesCommand(int id)
		{
			Id = id;
		}
	}
}
