using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public class RecordAttachmentScanResult
	{
		public RmsAttachmentScanState State { get; set; } = RmsAttachmentScanState.Skipped;
		public string Detail { get; set; }
	}

	/// <summary>
	/// Malware/content scan seam for Records attachments (RMS plan section 4.7). The default registration is a
	/// null scanner that reports <see cref="RmsAttachmentScanState.Skipped"/>; a real provider replaces it in the
	/// Autofac module. Bytes reach the scanner only after media hygiene has re-encoded images and refused active
	/// content, and a Rejected result stops the attachment from being stored.
	/// </summary>
	public interface IRecordAttachmentScanner
	{
		Task<RecordAttachmentScanResult> ScanAsync(string fileName, string contentType, byte[] data, CancellationToken cancellationToken = default);
	}
}
