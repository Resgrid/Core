using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Capture and read of immutable evidence artifacts (RMS plan sections 4.5, 5.2; registry M0169, RMS-3).
	/// <para>
	/// The plan requires every one of the six sources to prove authorization, provenance, classification, checksum
	/// and retention behaviour. Those five live here rather than in the adapters, so a new source cannot ship with
	/// a weaker rule than the others by accident: an adapter only decides what a bounded snapshot of its subsystem
	/// looks like, and this service decides what happens to it.
	/// </para>
	/// </summary>
	public class RecordsEvidenceService : IRecordsEvidenceService
	{
		private readonly IRmsEvidenceArtifactsRepository _artifacts;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsIncidentReportsRepository _incidentReports;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IUnitOfWork _unitOfWork;
		private readonly IEnumerable<IRecordEvidenceAdapter> _adapters;

		public RecordsEvidenceService(IRmsEvidenceArtifactsRepository artifacts, IRmsOperationalRecordsRepository records,
			IRmsIncidentReportsRepository incidentReports, IRmsAccessAuditsRepository audits, IUnitOfWork unitOfWork,
			IEnumerable<IRecordEvidenceAdapter> adapters)
		{
			_artifacts = artifacts;
			_records = records;
			_incidentReports = incidentReports;
			_audits = audits;
			_unitOfWork = unitOfWork;
			_adapters = adapters ?? Enumerable.Empty<IRecordEvidenceAdapter>();
		}

		public async Task<List<RecordEvidenceSourceState>> GetSourceStatesAsync(int departmentId)
		{
			var states = new List<RecordEvidenceSourceState>();

			// Every kind is reported, present or not: an author who cannot see that readiness evidence is
			// unavailable will assume there was none, which is a different and wrong conclusion.
			foreach (RmsEvidenceKind kind in Enum.GetValues(typeof(RmsEvidenceKind)))
			{
				var adapter = _adapters.FirstOrDefault(a => a.Kind == kind);
				if (adapter == null)
				{
					states.Add(new RecordEvidenceSourceState { Kind = kind, Available = false, Reason = "No adapter is registered for this source." });
					continue;
				}

				try
				{
					var available = await adapter.IsAvailableAsync(departmentId);
					states.Add(new RecordEvidenceSourceState { Kind = kind, Available = available, Reason = available ? null : "The source subsystem is not available for this department." });
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Evidence source {kind} could not report availability for department {departmentId}.");
					states.Add(new RecordEvidenceSourceState { Kind = kind, Available = false, Reason = "The source subsystem could not be reached." });
				}
			}

			return states;
		}

		public async Task<RmsEvidenceArtifact> CaptureAsync(RecordEvidenceCaptureRequest request, bool canCaptureRestricted = true, CancellationToken cancellationToken = default)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			if (string.IsNullOrWhiteSpace(request.RecordId)) throw new ArgumentException("A record is required.", nameof(request));
			if (string.IsNullOrWhiteSpace(request.CaptureReason))
				throw new ArgumentException("A capture reason is required; evidence never enters an official record anonymously.", nameof(request));

			await RequireOpenRecordAsync(request);

			var adapter = _adapters.FirstOrDefault(a => a.Kind == request.Kind)
				?? throw new InvalidOperationException($"No evidence adapter is registered for {request.Kind}.");

			if (!await adapter.IsAvailableAsync(request.DepartmentId))
				throw new InvalidOperationException($"{request.Kind} evidence is not available for this department.");

			var capture = await adapter.CaptureAsync(request, cancellationToken);
			if (capture == null || !capture.Available)
				throw new InvalidOperationException(capture?.UnavailableReason ?? $"{request.Kind} evidence could not be captured.");

			// Classification is the adapter's judgement about its own content, but the grant check is not: a member
			// without RecordRestricted_View must not be able to pull restricted content into a record they can read.
			if (capture.Classification != RmsEvidenceClassification.Unrestricted && !canCaptureRestricted)
				throw new InvalidOperationException("Capturing restricted evidence requires the restricted-records grant.");

			var manifestJson = Serialize(capture.Manifest);
			var now = DateTime.UtcNow;

			var artifact = new RmsEvidenceArtifact
			{
				RmsEvidenceArtifactId = Guid.NewGuid().ToString(),
				DepartmentId = request.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = request.RecordId,
				RecordKind = (int)request.RecordKind,
				RevisionId = null,
				Kind = (int)request.Kind,
				Title = Trim(capture.Title) ?? request.Kind.ToString(),
				CaptureReason = Trim(request.CaptureReason),
				SourceSubsystem = Trim(capture.SourceSubsystem),
				SourceEntityType = Trim(capture.SourceEntityType),
				SourceEntityId = Trim(capture.SourceEntityId),
				IdentifierScheme = Trim(capture.IdentifierScheme),
				SourceVersion = Trim(capture.SourceVersion),
				CoverageStart = capture.CoverageStart,
				CoverageEnd = capture.CoverageEnd,
				ManifestJson = manifestJson,
				Checksum = RecordSnapshotSerializer.Checksum(manifestJson),
				ByteSize = manifestJson == null ? 0 : Encoding.UTF8.GetByteCount(manifestJson),
				SourceItemCount = capture.SourceItemCount,
				Classification = (int)capture.Classification,
				RetentionYears = capture.RetentionYears,
				CapturedByUserId = request.CapturedByUserId,
				CapturedOn = now,
				OriginClient = (int)request.OriginClient,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			};

			await InTransactionAsync(async () =>
			{
				// A re-capture of the same source supersedes rather than replaces: the earlier artifact is what an
				// earlier revision attested to, and deleting it would rewrite history.
				var current = await _artifacts.GetCurrentDraftOfKindAsync(request.DepartmentId, request.RecordId, request.Kind, artifact.SourceEntityId);
				if (current != null)
				{
					current.SupersededByArtifactId = artifact.RmsEvidenceArtifactId;
					current.SupersededOn = now;
					current.ModifiedOn = now;
					current.RowVersion += 1;
					await _artifacts.UpdateAsync(current, cancellationToken, true);
				}

				await _artifacts.InsertAsync(artifact, cancellationToken, true);
				await AuditAsync(artifact, RmsAccessAuditAction.Change, "Evidence captured: " + request.Kind, cancellationToken);
			});

			return artifact;
		}

		public async Task<List<RmsEvidenceArtifact>> GetForRecordAsync(int departmentId, string recordId, string revisionId = null, bool includeSuperseded = false)
		{
			return (await _artifacts.GetForRecordAsync(departmentId, recordId, revisionId, includeSuperseded))?.ToList() ?? new List<RmsEvidenceArtifact>();
		}

		public Task<RmsEvidenceArtifact> GetAsync(int departmentId, string artifactId)
		{
			return _artifacts.GetByIdForDepartmentAsync(departmentId, artifactId);
		}

		public Task<int> BindToRevisionAsync(int departmentId, string recordId, string revisionId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(revisionId))
				throw new ArgumentException("A revision is required.", nameof(revisionId));

			return _artifacts.BindDraftToRevisionAsync(departmentId, recordId, revisionId, DateTime.UtcNow, cancellationToken);
		}

		public async Task<bool> VerifyAsync(int departmentId, string artifactId)
		{
			var artifact = await _artifacts.GetByIdForDepartmentAsync(departmentId, artifactId);
			if (artifact == null || string.IsNullOrWhiteSpace(artifact.Checksum))
				return false;

			return string.Equals(artifact.Checksum, RecordSnapshotSerializer.Checksum(artifact.ManifestJson ?? string.Empty), StringComparison.Ordinal);
		}

		/// <summary>
		/// Evidence attaches to a Record that exists and is still open. Attaching to a voided or cancelled Record
		/// would put supporting material behind a filing nobody stands behind any more.
		/// </summary>
		private async Task RequireOpenRecordAsync(RecordEvidenceCaptureRequest request)
		{
			if (request.RecordKind == RmsRecordKind.IncidentReport)
			{
				var report = await _incidentReports.GetByIdForDepartmentAsync(request.DepartmentId, request.RecordId);
				if (report == null || report.DeletedOn.HasValue)
					throw new InvalidOperationException($"Incident report {request.RecordId} does not exist in department {request.DepartmentId}.");
				if (RmsLifecycle.IsTerminal((RmsRecordState)report.State))
					throw new InvalidOperationException("Evidence cannot be captured against a voided or cancelled report.");
				return;
			}

			var record = await _records.GetByIdForDepartmentAsync(request.DepartmentId, request.RecordId);
			if (record == null || record.DeletedOn.HasValue)
				throw new InvalidOperationException($"Record {request.RecordId} does not exist in department {request.DepartmentId}.");
			if (RmsLifecycle.IsTerminal((RmsRecordState)record.State))
				throw new InvalidOperationException("Evidence cannot be captured against a voided or cancelled Record.");
		}

		/// <summary>
		/// Deterministic serialization: the same manifest must produce the same bytes, or the checksum an auditor
		/// re-computes years later will not match the one that was stored.
		/// </summary>
		public static string Serialize(object manifest)
		{
			if (manifest == null)
				return "{}";

			return JsonConvert.SerializeObject(manifest, new JsonSerializerSettings
			{
				Formatting = Formatting.None,
				DateFormatHandling = DateFormatHandling.IsoDateFormat,
				DateTimeZoneHandling = DateTimeZoneHandling.Utc,
				NullValueHandling = NullValueHandling.Ignore
			});
		}

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

		private Task AuditAsync(RmsEvidenceArtifact artifact, RmsAccessAuditAction action, string purpose, CancellationToken cancellationToken)
		{
			return _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = artifact.DepartmentId,
				RecordId = artifact.RecordId,
				RevisionId = artifact.RevisionId,
				Action = (int)action,
				ActorUserId = artifact.CapturedByUserId,
				Purpose = purpose,
				OriginClient = artifact.OriginClient,
				Successful = true,
				OccurredOn = DateTime.UtcNow,
				DetailJson = JsonConvert.SerializeObject(new
				{
					artifact.RmsEvidenceArtifactId,
					artifact.Kind,
					artifact.SourceSubsystem,
					artifact.SourceEntityId,
					artifact.Checksum,
					artifact.SourceItemCount,
					artifact.Classification,
					artifact.CaptureReason
				})
			}, cancellationToken, true);
		}

		private async Task InTransactionAsync(Func<Task> work)
		{
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await work();
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}
	}
}
