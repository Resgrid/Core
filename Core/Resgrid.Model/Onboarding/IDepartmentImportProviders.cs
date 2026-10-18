using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Onboarding
{
	/// <summary>
	/// Object storage for import artifacts (wizard plan section 8.7, decision D1). Implemented once, on RustFS (any
	/// S3-compatible endpoint), by RustFsImportArtifactStore. The store only ever sees ciphertext produced by
	/// <see cref="IImportArtifactCipher"/>; object keys never contain a file name. When unconfigured it fails closed.
	/// </summary>
	public interface IImportArtifactStore
	{
		/// <summary>True when an endpoint and bucket are configured (ImportConfig.S3*).</summary>
		bool IsConfigured { get; }

		/// <summary>Startup/periodic health check: the bucket is reachable and writable. Never throws.</summary>
		Task<ImportStoreHealth> CheckHealthAsync(CancellationToken cancellationToken = default);

		/// <summary>Begins a multipart upload and returns its upload id.</summary>
		Task<string> StartUploadAsync(string objectKey, CancellationToken cancellationToken = default);

		/// <summary>Uploads one already-encrypted part and returns its ETag.</summary>
		Task<string> UploadPartAsync(string objectKey, string uploadId, int partNumber, byte[] content, CancellationToken cancellationToken = default);

		Task CompleteUploadAsync(string objectKey, string uploadId, IReadOnlyList<ImportArtifactPart> parts, CancellationToken cancellationToken = default);

		/// <summary>Aborts an incomplete multipart upload (abandoned uploads, section 13.1). Never throws for a missing upload.</summary>
		Task AbortUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken = default);

		/// <summary>Writes a small whole object (an extraction snapshot page or a generated report), already encrypted.</summary>
		Task PutAsync(string objectKey, byte[] content, CancellationToken cancellationToken = default);

		/// <summary>The stored (ciphertext) length of an object, or null when it does not exist.</summary>
		Task<long?> GetSizeAsync(string objectKey, CancellationToken cancellationToken = default);

		/// <summary>Reads <paramref name="length"/> stored bytes from <paramref name="offset"/> (ranged GET).</summary>
		Task<byte[]> ReadRangeAsync(string objectKey, long offset, int length, CancellationToken cancellationToken = default);

		Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);

		/// <summary>Deletes every object under a prefix; returns how many were removed.</summary>
		Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default);

		/// <summary>The department ids that have objects in the store (the orphan sweep after department deletion).</summary>
		Task<IReadOnlyList<int>> ListDepartmentIdsAsync(CancellationToken cancellationToken = default);
	}

	public sealed class ImportStoreHealth
	{
		public bool Configured { get; set; }
		public bool Healthy { get; set; }
		/// <summary>Value-free reason ("not-configured", "bucket-missing", "unreachable").</summary>
		public string Reason { get; set; }
		public DateTime CheckedOn { get; set; }
	}

	/// <summary>
	/// Client-side chunked AEAD for artifacts (plan section 8.7): AES-256-GCM per segment with a random nonce, the
	/// artifact id, segment index and final flag bound into the associated data, so segments cannot be reordered,
	/// swapped between artifacts or truncated without detection.
	/// </summary>
	public interface IImportArtifactCipher
	{
		/// <summary>A fresh 256-bit data key, base64.</summary>
		string NewDataKey();

		/// <summary>Encrypts one segment (one upload chunk and one multipart part).</summary>
		byte[] EncryptSegment(string dataKey, string artifactId, long segmentIndex, bool isFinal, ReadOnlySpan<byte> plaintext);

		/// <summary>A read-only, seekable plaintext view over an encrypted object, decrypting one segment at a time.</summary>
		Task<Stream> OpenReadAsync(IImportArtifactStore store, string objectKey, string dataKey, string artifactId, CancellationToken cancellationToken = default);

		/// <summary>Encrypts a whole small payload as a single final segment (snapshots, reports, staged rows).</summary>
		byte[] EncryptWhole(string dataKey, string artifactId, byte[] plaintext);

		/// <summary>Decrypts a payload written by <see cref="EncryptWhole"/>; throws ImportArtifactIntegrityException on tampering.</summary>
		byte[] DecryptWhole(string dataKey, string artifactId, byte[] content);
	}

	/// <summary>Format sniffing and the L0 signature rules (plan section 14.1).</summary>
	public sealed class ImportFileInspection
	{
		public ImportFileFormat Format { get; set; }
		/// <summary>A stable L0 issue code when the file must be refused ("IMP-FILE-MACRO"), else null.</summary>
		public string RejectionCode { get; set; }
		public string Encoding { get; set; }
		public string Delimiter { get; set; }
		public bool RequiresScanning { get; set; }
	}

	/// <summary>One sheet (or the single table of a CSV/JSON file).</summary>
	public sealed class ImportSheetInfo
	{
		public int Index { get; set; }
		public string Name { get; set; }
		public bool Hidden { get; set; }
	}

	public sealed class ImportReadOptions
	{
		public int MaxRows { get; set; } = 20000;
		public int MaxColumns { get; set; } = 512;
		public int MaxFieldLength { get; set; } = 32 * 1024;
		/// <summary>CSV only: the administrator confirmed the Windows-1252 fallback for a file that is not valid UTF-8.</summary>
		public bool AllowWindows1252 { get; set; }
		public string Delimiter { get; set; }
		public TimeSpan TimeBudget { get; set; } = TimeSpan.FromMinutes(5);
	}

	/// <summary>The result of reading one sheet: physical rows as strings (cached values only; formulas never run).</summary>
	public sealed class ImportSheetReadResult
	{
		/// <summary>A stable L1 issue code when the sheet could not be read ("IMP-PARSE-COLUMNS"), else null.</summary>
		public string ErrorCode { get; set; }
		/// <summary>1-based physical row number of the error, when known.</summary>
		public int? ErrorRow { get; set; }
		public bool Truncated { get; set; }
		public int RowsRead { get; set; }
	}

	/// <summary>
	/// Reads CSV/TSV, XLSX and JSON/JSONL into rows of strings (plan section 8.3 Readers). Values are never echoed into
	/// errors: failures carry issue codes and row numbers only (section 14.6).
	/// </summary>
	public interface IImportFileReader
	{
		ImportFileInspection Inspect(Stream content, string fileName);

		IReadOnlyList<ImportSheetInfo> ListSheets(Stream content, ImportFileFormat format);

		/// <summary>
		/// Streams a sheet's physical rows to <paramref name="onRow"/> (row number, cells). XLSX date cells are rendered
		/// ISO-8601 using the workbook's 1900/1904 date system.
		/// </summary>
		ImportSheetReadResult ReadSheet(Stream content, ImportFileFormat format, int sheetIndex, ImportReadOptions options,
			Action<int, string[]> onRow, CancellationToken cancellationToken = default);
	}

	/// <summary>Column definitions for a generated template sheet.</summary>
	public sealed class ImportTemplateSheet
	{
		public string Name { get; set; }
		public string EntityKey { get; set; }
		public List<ImportTemplateColumn> Columns { get; set; } = new List<ImportTemplateColumn>();
		/// <summary>Example rows, marked so they are skipped on import.</summary>
		public List<string[]> ExampleRows { get; set; } = new List<string[]>();
		/// <summary>Real rows ("download current data"), written after the examples without the marker so they import.</summary>
		public List<string[]> DataRows { get; set; } = new List<string[]>();
	}

	public sealed class ImportTemplateColumn
	{
		public string Header { get; set; }
		public string Note { get; set; }
		public bool Required { get; set; }
		/// <summary>Name of a Lists-sheet column that becomes a drop-down, or null.</summary>
		public string ListName { get; set; }
		/// <summary>"text", "date", "number", "boolean".</summary>
		public string CellType { get; set; } = "text";
	}

	public sealed class ImportTemplateWorkbook
	{
		public string Title { get; set; }
		public List<string> Instructions { get; set; } = new List<string>();
		public List<ImportTemplateSheet> Sheets { get; set; } = new List<ImportTemplateSheet>();
		/// <summary>Named drop-down lists (enum values and the department's existing groups, roles, types…).</summary>
		public Dictionary<string, List<string>> Lists { get; set; } = new Dictionary<string, List<string>>();
	}

	/// <summary>Renders the starter workbook and per-entity CSV templates (plan section 8.3). Never writes formulas.</summary>
	public interface IImportTemplateWriter
	{
		byte[] WriteWorkbook(ImportTemplateWorkbook workbook);

		/// <summary>A CSV template with formula-neutralized cells.</summary>
		byte[] WriteCsv(ImportTemplateSheet sheet);
	}

	/// <summary>A helpdesk ticket request (Freshdesk; plan section 17.5). Metadata only (conformance I15).</summary>
	public sealed class HelpdeskTicketRequest
	{
		public string RequesterEmail { get; set; }
		public string RequesterName { get; set; }
		public string Subject { get; set; }
		/// <summary>Plain text: department id and name, category, the administrator's own text, issue codes and counts, the BackOffice link.</summary>
		public string Description { get; set; }
		public int Priority { get; set; }
		public List<string> Tags { get; set; } = new List<string>();
	}

	public sealed class HelpdeskTicketResult
	{
		public bool Success { get; set; }
		public string TicketId { get; set; }
		public string TicketUrl { get; set; }
		/// <summary>True when the failure is worth retrying (timeout, 5xx, 429).</summary>
		public bool Transient { get; set; }
		public string ErrorCode { get; set; }
	}

	public interface IHelpdeskProvider
	{
		bool IsConfigured { get; }

		Task<HelpdeskTicketResult> CreateTicketAsync(HelpdeskTicketRequest request, CancellationToken cancellationToken = default);

		/// <summary>Adds a note (a staff reply mirrored from the workspace thread) to a ticket.</summary>
		Task<bool> AddNoteAsync(string ticketId, string body, CancellationToken cancellationToken = default);
	}

	/// <summary>An artifact failed authentication, framing or retrieval; carries a stable issue code, never content.</summary>
	public sealed class ImportArtifactIntegrityException : IOException
	{
		public ImportArtifactIntegrityException(string code) : base(code)
		{
			Code = code;
		}

		public string Code { get; }
	}
}
