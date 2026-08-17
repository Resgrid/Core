using System;
using ProtoBuf;

namespace Resgrid.Model.Queue
{
	/// <summary>
	/// Carries a started communication test run onto the bus so the whole run — building the
	/// per-recipient result rows and then sending on every enabled channel — happens in the worker
	/// process instead of on the request thread that started it. A large department is hundreds of
	/// sequential provider round-trips, which no web request can absorb.
	/// The run row already exists when this is published; the worker owns everything after it.
	/// </summary>
	[ProtoContract]
	public class CommunicationTestQueueItem
	{
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		/// <summary>The run to build and deliver. Stored as a string because ProtoBuf has no Guid.</summary>
		[ProtoMember(2)]
		public string CommunicationTestRunId { get; set; }

		[ProtoMember(3)]
		public string CommunicationTestId { get; set; }

		public Guid GetRunId()
		{
			return Guid.TryParse(CommunicationTestRunId, out var runId) ? runId : Guid.Empty;
		}
	}
}
