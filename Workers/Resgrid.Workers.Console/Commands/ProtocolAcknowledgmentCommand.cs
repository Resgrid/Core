using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker 86: protocol file text extraction, quiz-gated acknowledgment completion, acknowledgment reminders and escalation.</summary>
	public class ProtocolAcknowledgmentCommand : IQuidjiboCommand
	{
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }

		public ProtocolAcknowledgmentCommand(int id)
		{
			Id = id;
		}
	}
}
