using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Models.Records
{
	public class RecordEvidenceView
	{
		public RecordEvidenceContext Context { get; set; }
		public List<RecordEvidenceArtifactView> Artifacts { get; set; } = new();
		public string Message { get; set; }
		public int Page { get; set; }
		public bool HasMore { get; set; }
	}
	public class RecordEvidenceArtifactView
	{
		public string Id { get; set; }
		public string Title { get; set; }
		public string Reason { get; set; }
		public string Checksum { get; set; }
		public string SourceVersion { get; set; }
		public string RevisionId { get; set; }
		public DateTime CapturedOn { get; set; }
		public int Items { get; set; }
		public bool Superseded { get; set; }
		public bool Withheld { get; set; }
	}
	public class RecordEvidenceSelectionView
	{
		public RecordEvidenceSelection Selection { get; set; }
		public RecordEvidenceForm Input { get; set; } = new();
	}
	public class RecordEvidenceForm
	{
		[Required] public string RecordId { get; set; }
		public RmsRecordKind RecordKind { get; set; }
		public RmsEvidenceKind SourceKind { get; set; }
		[Required] public long? RowVersion { get; set; }
		[Required, StringLength(500)] public string CaptureReason { get; set; }
		public DateTime? StartUtc { get; set; }
		public DateTime? EndUtc { get; set; }
		public List<int> UnitIds { get; set; } = new();
		public List<string> UserIds { get; set; } = new();
		public List<string> SourceIds { get; set; } = new();
	}
}
