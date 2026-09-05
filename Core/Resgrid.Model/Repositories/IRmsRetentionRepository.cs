using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public sealed class RmsPurgeResult
	{
		/// <summary>SQL content was removed. Search storage is complete only after its separate durable acknowledgement.</summary>
		public bool Purged { get; set; }
		public bool SearchErasurePending { get; set; }
		public bool Held { get; set; }
		public int AttachmentsPurged { get; set; }
		public string Reason { get; set; }
	}

	public sealed class RmsSearchErasureTarget
	{
		public int DepartmentId { get; set; }
		public int RecordKind { get; set; }
		public string RecordId { get; set; }
		public DateTime PurgedOn { get; set; }
		public List<string> SourceIds { get; set; } = new List<string>();
	}

	public interface IRmsRetentionRepository
	{
		/// <summary>Rechecks the current policy, all holds, and aggregate version under one department transaction lock.</summary>
		Task<RmsPurgeResult> PurgeAsync(int departmentId, string recordId, RmsRecordKind kind, long expectedVersion, DateTime now, CancellationToken cancellationToken = default);
		Task<List<RmsSearchErasureTarget>> GetPendingSearchErasuresAsync(int take, RmsSearchErasureTarget after = null, CancellationToken cancellationToken = default);
		/// <summary>Called only after all source keys have been removed, deleted Lucene documents expunged and the index committed.</summary>
		Task<bool> CompleteSearchErasureAsync(RmsSearchErasureTarget target, DateTime completedOn, CancellationToken cancellationToken = default);
	}
}
