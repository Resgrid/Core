using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>How a bulk packet is compiled (RMS plan section 4.7, "Bulk operations and packets").</summary>
	public enum RecordsBulkPacketMode
	{
		/// <summary>One compiled PDF: cover page with manifest, then every record in selection order.</summary>
		CompiledPdf = 1,
		/// <summary>A zip bundle of per-record PDFs plus a manifest.json.</summary>
		Bundle = 2
	}

	/// <summary>A bounded bulk print/export over an authorized selection; never bulk void or bulk delete.</summary>
	public class RecordsBulkPacketRequest
	{
		/// <summary>Most records one packet may compile; a packet is a deliverable, not an export of the archive.</summary>
		public const int MaxRecords = 200;

		public List<string> RecordIds { get; set; } = new List<string>();
		public RecordsBulkPacketMode Mode { get; set; } = RecordsBulkPacketMode.CompiledPdf;
		public string Title { get; set; }
		/// <summary>Why the packet is produced (accreditation, insurance, discovery, board packet); audited per record.</summary>
		public string Purpose { get; set; }
		/// <summary>Optional delivery through the scheduled-report email path; the stored run stays downloadable either way.</summary>
		public string DeliverToEmail { get; set; }
		public RmsOriginClient OriginClient { get; set; } = RmsOriginClient.Web;
	}

	public class RecordsBulkAssignRequest
	{
		public List<string> RecordIds { get; set; } = new List<string>();
		public string ReviewerUserId { get; set; }
		public string Reason { get; set; }
	}

	public class RecordsBulkResult
	{
		public int Processed { get; set; }
		public int Skipped { get; set; }
		public List<RecordsBulkSkip> Skips { get; set; } = new List<RecordsBulkSkip>();
		/// <summary>The stored run for a packet (download through the export runs surface); null for an assignment.</summary>
		public RmsExportRun Run { get; set; }
		public bool Delivered { get; set; }
	}

	public class RecordsBulkSkip
	{
		public string RecordId { get; set; }
		public string Reason { get; set; }
	}
}
