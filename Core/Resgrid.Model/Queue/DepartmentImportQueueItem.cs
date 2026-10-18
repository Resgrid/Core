using System;
using ProtoBuf;

namespace Resgrid.Model.Queue
{
	/// <summary>
	/// The start nudge for a Department Import run (departmentimport queue, wizard plan section 10.4). Identifiers only:
	/// the worker reloads the run, checks its department and re-authorizes before doing anything. A lost nudge only delays
	/// the start until worker 47's minute sweep claims the run.
	/// </summary>
	[ProtoContract]
	public class DepartmentImportQueueItem
	{
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		/// <summary>The ImportRuns id (a string GUID; ProtoBuf has no Guid).</summary>
		[ProtoMember(2)]
		public string RunId { get; set; }

		[ProtoMember(3)]
		public DateTime QueuedOnUtc { get; set; }
	}
}
