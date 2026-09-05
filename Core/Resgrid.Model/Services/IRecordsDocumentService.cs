using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public sealed class RecordDocument
	{
		public string Format { get; set; } = "resgrid.department-record.v2";
		public string RecordId { get; set; }
		public RmsRecordKind RecordKind { get; set; }
		public string RecordNumber { get; set; }
		public string RevisionId { get; set; }
		public int RevisionNumber { get; set; }
		public string OriginalChecksum { get; set; }
		public string ContentChecksum { get; set; }
		public DateTime FinalizedOn { get; set; }
		public string AttestedBy { get; set; }
		public string AttestationVersion { get; set; }
		public string ContentJson { get; set; }
		public List<string> WithheldFields { get; set; } = new List<string>();
	}
	public interface IRecordsDocumentService
	{
		/// <summary>Defaults to the official current revision, even while an amendment is open. Live authorization is always required.</summary>
		Task<RecordDocument> GetAsync(int departmentId, string userId, string recordId, RmsRecordKind kind, string revisionId = null, bool exporting = false);
		Task<string> RenderHtmlAsync(int departmentId, string userId, RecordDocument document);
		Task<byte[]> RenderPdfAsync(int departmentId, string userId, RecordDocument document);
		Task<byte[]> RenderDiffPdfAsync(int departmentId, string userId, string recordId, RmsRecordKind kind, string fromRevisionId, string toRevisionId);
		Task<List<RecordFieldDiff>> DiffAsync(int departmentId, string userId, RecordDocument from, RecordDocument to);
	}
}
