using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IRecordEvidenceSelectionService
	{
		Task<RecordEvidenceContext> GetContextAsync(int departmentId, string userId, string recordId, RmsRecordKind recordKind);
		Task<RecordEvidenceSelection> GetAsync(int departmentId, string userId, string recordId, RmsRecordKind recordKind,
			RmsEvidenceKind sourceKind, string channelId = null, long afterSequence = 0);
	}

	public class RecordEvidenceContext
	{
		public string RecordId { get; set; }
		public RmsRecordKind RecordKind { get; set; }
		public string RecordNumber { get; set; }
		public long RowVersion { get; set; }
		public int? CallId { get; set; }
		public DateTime? StartUtc { get; set; }
		public DateTime? EndUtc { get; set; }
		public bool CanCapture { get; set; }
		public bool CanViewRestricted { get; set; }
		public bool CanExport { get; set; }
	}

	public class RecordEvidenceSelection
	{
		public RecordEvidenceContext Context { get; set; }
		public RmsEvidenceKind SourceKind { get; set; }
		public List<RecordEvidenceSourceState> Sources { get; set; } = new();
		public List<RecordEvidenceChoice> Choices { get; set; } = new();
		public List<RecordEvidenceChoice> Channels { get; set; } = new();
		public string ChannelId { get; set; }
		public long? NextSequence { get; set; }
	}

	public class RecordEvidenceChoice
	{
		public string Id { get; set; }
		public string Label { get; set; }
		public string Body { get; set; }
		public DateTime? OccurredOn { get; set; }
		public DateTime? EditedOn { get; set; }
		public long? Sequence { get; set; }
	}
}
