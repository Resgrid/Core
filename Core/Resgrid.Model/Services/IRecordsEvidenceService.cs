using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// One evidence source's capture rule (RMS plan section 4.5, RMS-3). An adapter reads its own subsystem,
	/// decides what an authorized snapshot of it looks like, and hands back a bounded manifest. It never writes,
	/// never persists, and never returns a live handle to the source — <see cref="IRecordsEvidenceService"/> owns
	/// serialization, checksum, classification, retention and audit so those cannot vary per source.
	/// </summary>
	public interface IRecordEvidenceAdapter
	{
		RmsEvidenceKind Kind { get; }

		/// <summary>
		/// False when the source subsystem is not present in this build or not configured for the department.
		/// The adapter still ships — the plan requires all six — and says why it has nothing rather than
		/// pretending the evidence does not exist.
		/// </summary>
		Task<bool> IsAvailableAsync(int departmentId);

		Task<RecordEvidenceCapture> CaptureAsync(RecordEvidenceCaptureRequest request, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Capture and read of immutable evidence artifacts (RMS plan sections 4.5, 5.2; registry M0169).
	/// <para>
	/// Every capture proves the same five things regardless of source, which is why they run through one service:
	/// the caller was authorized, the provenance is recorded, the classification is decided at capture, the
	/// content is checksummed, and the retention rule is attached. An artifact is never updated; a correction
	/// supersedes it and both remain readable.
	/// </para>
	/// </summary>
	public interface IRecordsEvidenceService
	{
		/// <summary>Which of the six sources can actually produce evidence for this department right now.</summary>
		Task<List<RecordEvidenceSourceState>> GetSourceStatesAsync(int departmentId);

		/// <summary>
		/// Captures one artifact. Throws when the Record is missing or terminal, when the reason is absent, or
		/// when the caller lacks the restricted grant for a source that classifies restricted.
		/// </summary>
		Task<RmsEvidenceArtifact> CaptureAsync(RecordEvidenceCaptureRequest request, bool canCaptureRestricted = true, CancellationToken cancellationToken = default);

		/// <summary>Artifacts on the working draft, or on a revision when one is named.</summary>
		Task<List<RmsEvidenceArtifact>> GetForRecordAsync(int departmentId, string recordId, string revisionId = null, bool includeSuperseded = false);

		Task<RmsEvidenceArtifact> GetAsync(int departmentId, string artifactId);

		/// <summary>Binds the draft's artifacts to a revision at finalize; artifacts already bound are untouched.</summary>
		Task<int> BindToRevisionAsync(int departmentId, string recordId, string revisionId, CancellationToken cancellationToken = default);

		/// <summary>Re-computes the checksum from the stored manifest; false means the artifact was tampered with.</summary>
		Task<bool> VerifyAsync(int departmentId, string artifactId);
	}

	/// <summary>Whether one evidence source can produce anything for a department, and why not when it cannot.</summary>
	public class RecordEvidenceSourceState
	{
		public RmsEvidenceKind Kind { get; set; }
		public bool Available { get; set; }
		public string Reason { get; set; }
	}
}
