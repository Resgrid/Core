using System;
using System.Collections.Generic;
using Quidjibo.Commands;

namespace Resgrid.Workers.Console.Commands
{
	/// <summary>Worker ID 31 (Identifier Allocation Registry, Workforce &amp; Business Operations plan C7): bid expiration.</summary>
	public sealed class BidExpirationCommand : IQuidjiboCommand
	{
		public BidExpirationCommand(int id) { Id = id; }
		public int Id { get; }
		public Guid? CorrelationId { get; set; }
		public Dictionary<string, string> Metadata { get; set; }
	}
}
