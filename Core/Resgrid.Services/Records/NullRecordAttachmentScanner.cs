using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Services.Records
{
	/// <summary>Default scanner: no engine configured, so every attachment is stored as Skipped and stays eligible for a later scan.</summary>
	public class NullRecordAttachmentScanner : IRecordAttachmentScanner
	{
		public const string NoScannerDetail = "No attachment scanner is configured.";

		public Task<RecordAttachmentScanResult> ScanAsync(string fileName, string contentType, byte[] data, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Skipped, Detail = NoScannerDetail });
		}
	}
}
