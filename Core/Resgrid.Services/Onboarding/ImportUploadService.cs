using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Chunked, resumable uploads into the RustFS artifact store (wizard plan sections 8.7 and 14.1). The browser sends
	/// fixed-size chunks in order; each is SHA-256 checked, encrypted as one AES-GCM segment and uploaded as one multipart
	/// part, and the part list on ImportArtifacts lets an interrupted upload resume after the last confirmed part. No file
	/// byte ever reaches the relational database. When the store is not configured, uploads are refused (fail closed).
	/// </summary>
	public class ImportUploadService : IImportUploadService
	{
		private static readonly string[] TextExtensions = { ".csv", ".tsv", ".txt", ".tab", ".json", ".jsonl", ".ndjson", ".geojson" };
		private static readonly string[] ScannedExtensions = { ".xlsx", ".zip", ".kml", ".kmz", ".gpx" };

		private readonly IImportArtifactsRepository _artifacts;
		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportArtifactStore _store;
		private readonly IImportArtifactCipher _cipher;
		private readonly IImportKeyService _keys;
		private readonly IImportRunQueue _runQueue;
		private readonly IImportTimelineService _timeline;
		private readonly ISubscriptionsService _subscriptions;

		public ImportUploadService(IImportArtifactsRepository artifacts, IImportWorkspacesRepository workspaces, IImportArtifactStore store, IImportArtifactCipher cipher,
			IImportKeyService keys, IImportRunQueue runQueue, IImportTimelineService timeline, ISubscriptionsService subscriptions)
		{
			_artifacts = artifacts;
			_workspaces = workspaces;
			_store = store;
			_cipher = cipher;
			_keys = keys;
			_runQueue = runQueue;
			_timeline = timeline;
			_subscriptions = subscriptions;
		}

		public static int ChunkSize => Math.Max(5 * 1024 * 1024, ImportConfig.ChunkSizeBytes);

		public async Task<ImportUploadStatus> StartAsync(int departmentId, string userId, string workspaceId, string fileName, long sizeBytes, string sha256,
			string contentType, string sourceId, bool staffAuthored = false, CancellationToken cancellationToken = default)
		{
			if (!_store.IsConfigured)
				throw new DepartmentImportException("IMP-STORE-UNAVAILABLE", 503);

			var workspace = await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);
			if (DepartmentImportStateMachine.IsTerminal((ImportWorkspaceStatus)workspace.Status))
				throw new DepartmentImportException("import_workspace_closed", 409);

			var safeName = Clip(FileHelper.GetSafeFileName(Path.GetFileName(fileName ?? string.Empty)), 255);
			var extension = (Path.GetExtension(safeName) ?? string.Empty).ToLowerInvariant();
			if (!TextExtensions.Contains(extension) && !ScannedExtensions.Contains(extension))
				throw new DepartmentImportException("IMP-FILE-UNSUPPORTED");
			if (ScannedExtensions.Contains(extension) && !AttachmentScanningConfig.Enabled)
				// Decision D6: without malware scanning only text formats are accepted.
				throw new DepartmentImportException("IMP-FILE-SCANNING-REQUIRED");

			if (sizeBytes <= 0)
				throw new DepartmentImportException("IMP-FILE-EMPTY");

			var paid = await IsPaidAsync(departmentId);
			var maxFile = paid ? ImportConfig.PaidPlanMaxArtifactBytes : ImportConfig.FreePlanMaxArtifactBytes;
			if (sizeBytes > maxFile)
				throw new DepartmentImportException("IMP-FILE-TOO-LARGE");

			var used = (await _artifacts.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
				.Where(a => !a.PurgedOn.HasValue && a.UploadState != (int)ImportArtifactUploadState.Aborted && a.UploadState != (int)ImportArtifactUploadState.Expired)
				.Sum(a => Math.Max(a.ExpectedSizeBytes, a.SizeBytes));
			if (used + sizeBytes > (paid ? ImportConfig.PaidPlanMaxWorkspaceBytes : ImportConfig.FreePlanMaxWorkspaceBytes))
				throw new DepartmentImportException("IMP-WORKSPACE-FULL");

			var (wrapped, _) = await _keys.CreateKeyAsync(departmentId);
			var now = DateTime.UtcNow;
			var artifact = new ImportArtifact
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				SourceId = string.IsNullOrWhiteSpace(sourceId) ? null : sourceId,
				Kind = (int)ImportArtifactKind.Upload,
				OriginalName = safeName,
				DeclaredContentType = Clip(contentType, 128),
				ExpectedSizeBytes = sizeBytes,
				Sha256Declared = IsHex64(sha256) ? sha256.ToLowerInvariant() : null,
				ScanState = (int)ImportArtifactScanState.Pending,
				WrappedDataKey = wrapped,
				UploadState = (int)ImportArtifactUploadState.InProgress,
				UploadedParts = "[]",
				StaffAuthored = staffAuthored,
				CreatedBy = userId,
				CreatedOn = now,
				UpdatedOn = now
			};
			artifact.ObjectKey = ImportArtifactKeys.ForArtifact(departmentId, workspaceId, artifact.Id);
			artifact.MultipartUploadId = await _store.StartUploadAsync(artifact.ObjectKey, cancellationToken);

			await _artifacts.InsertAsync(artifact, cancellationToken, true);
			await _workspaces.TouchAsync(departmentId, workspaceId, now, cancellationToken);
			await _timeline.RecordAsync(departmentId, workspaceId, staffAuthored ? ImportActorKind.ResgridStaff : ImportActorKind.Administrator, userId, "UploadStarted",
				new { artifactId = artifact.Id, sizeBytes }, cancellationToken: cancellationToken);

			return Status(artifact);
		}

		public async Task<ImportUploadStatus> AppendAsync(int departmentId, string artifactId, int partNumber, byte[] chunk, string chunkSha256,
			CancellationToken cancellationToken = default)
		{
			var artifact = await _artifacts.GetByIdAsync(departmentId, artifactId, cancellationToken) ?? throw new DepartmentImportException("import_artifact_not_found", 404);
			var parts = Parts(artifact);

			if (artifact.UploadState != (int)ImportArtifactUploadState.InProgress)
				throw new DepartmentImportException(artifact.UploadState == (int)ImportArtifactUploadState.Completed ? "import_upload_complete" : "IMP-UPLOAD-EXPIRED", 409);
			if (chunk == null || chunk.Length == 0)
				throw new DepartmentImportException("IMP-UPLOAD-CHUNK-EMPTY");

			var hash = Convert.ToHexString(SHA256.HashData(chunk)).ToLowerInvariant();
			if (!string.Equals(hash, chunkSha256?.Trim(), StringComparison.OrdinalIgnoreCase))
				throw new DepartmentImportException("IMP-UPLOAD-CHUNK-HASH");

			// A part already received with the same bytes is acknowledged (the browser retried after a lost response).
			var existing = parts.FirstOrDefault(p => p.PartNumber == partNumber);
			if (existing != null)
			{
				if (existing.Sha256 == hash)
					return Status(artifact);
				throw new DepartmentImportException("IMP-UPLOAD-CHUNK-CONFLICT", 409);
			}

			if (partNumber != parts.Count + 1)
				throw new DepartmentImportException("IMP-UPLOAD-CHUNK-ORDER", 409);

			var isFinal = artifact.SizeBytes + chunk.Length == artifact.ExpectedSizeBytes;
			if (!isFinal && chunk.Length != ChunkSize)
				throw new DepartmentImportException("IMP-UPLOAD-CHUNK-SIZE");
			if (artifact.SizeBytes + chunk.Length > artifact.ExpectedSizeBytes)
				throw new DepartmentImportException("IMP-UPLOAD-TOO-MUCH");

			var key = await _keys.UnwrapAsync(departmentId, artifact.WrappedDataKey);
			var segment = _cipher.EncryptSegment(key, artifact.Id, partNumber - 1, isFinal, chunk);
			var etag = await _store.UploadPartAsync(artifact.ObjectKey, artifact.MultipartUploadId, partNumber, segment, cancellationToken);

			parts.Add(new ImportArtifactPart { PartNumber = partNumber, Size = chunk.Length, Sha256 = hash, ETag = etag });
			var newSize = artifact.SizeBytes + chunk.Length;
			if (!await _artifacts.TryAppendPartAsync(departmentId, artifactId, artifact.SizeBytes, newSize, JsonConvert.SerializeObject(parts), DateTime.UtcNow, cancellationToken))
				throw new DepartmentImportException("IMP-UPLOAD-CHUNK-ORDER", 409);

			artifact.SizeBytes = newSize;
			artifact.UploadedParts = JsonConvert.SerializeObject(parts);
			return Status(artifact);
		}

		public async Task<ImportUploadStatus> CompleteAsync(int departmentId, string userId, string artifactId, CancellationToken cancellationToken = default)
		{
			var artifact = await _artifacts.GetByIdAsync(departmentId, artifactId, cancellationToken) ?? throw new DepartmentImportException("import_artifact_not_found", 404);
			if (artifact.UploadState == (int)ImportArtifactUploadState.Completed)
				return Status(artifact);
			if (artifact.UploadState != (int)ImportArtifactUploadState.InProgress)
				throw new DepartmentImportException("IMP-UPLOAD-EXPIRED", 409);
			if (artifact.SizeBytes != artifact.ExpectedSizeBytes)
				throw new DepartmentImportException("IMP-UPLOAD-INCOMPLETE", 409);

			await _store.CompleteUploadAsync(artifact.ObjectKey, artifact.MultipartUploadId, Parts(artifact), cancellationToken);

			artifact.UploadState = (int)ImportArtifactUploadState.Completed;
			artifact.CompletedOn = DateTime.UtcNow;
			artifact.UpdatedOn = artifact.CompletedOn.Value;
			await _artifacts.UpdateAsync(artifact, cancellationToken, true);

			// The worker re-verifies the whole-file hash while decrypting, applies the L0 rules and scans before parsing.
			var run = await _runQueue.QueueAsync(new ImportRun
			{
				DepartmentId = departmentId,
				WorkspaceId = artifact.WorkspaceId,
				Kind = (int)ImportRunKind.Analysis,
				ArtifactId = artifact.Id,
				RequestedBy = userId
			}, cancellationToken);

			await _timeline.RecordAsync(departmentId, artifact.WorkspaceId, artifact.StaffAuthored ? ImportActorKind.ResgridStaff : ImportActorKind.Administrator, userId,
				"UploadCompleted", new { artifactId, sizeBytes = artifact.SizeBytes, runId = run.Id }, run.CorrelationId, cancellationToken);

			var status = Status(artifact);
			status.AnalysisRunId = run.Id;
			return status;
		}

		public async Task<ImportUploadStatus> GetStatusAsync(int departmentId, string artifactId, CancellationToken cancellationToken = default)
		{
			var artifact = await _artifacts.GetByIdAsync(departmentId, artifactId, cancellationToken) ?? throw new DepartmentImportException("import_artifact_not_found", 404);
			return Status(artifact);
		}

		public async Task<int> AbortStaleUploadsAsync(CancellationToken cancellationToken = default)
		{
			var stale = await _artifacts.GetStaleUploadsAsync(DateTime.UtcNow.AddHours(-ImportConfig.AbandonedUploadHours), 100, cancellationToken);
			var aborted = 0;
			foreach (var artifact in stale)
			{
				try
				{
					await _store.AbortUploadAsync(artifact.ObjectKey, artifact.MultipartUploadId, cancellationToken);
					artifact.UploadState = (int)ImportArtifactUploadState.Expired;
					artifact.PurgedOn = DateTime.UtcNow;
					artifact.UpdatedOn = DateTime.UtcNow;
					await _artifacts.UpdateAsync(artifact, cancellationToken, true);
					aborted++;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Abandoned import upload {artifact.Id} could not be aborted; retried next hour");
				}
			}

			return aborted;
		}

		private static List<ImportArtifactPart> Parts(ImportArtifact artifact) =>
			string.IsNullOrWhiteSpace(artifact.UploadedParts) ? new List<ImportArtifactPart>() : JsonConvert.DeserializeObject<List<ImportArtifactPart>>(artifact.UploadedParts);

		private static ImportUploadStatus Status(ImportArtifact artifact) => new ImportUploadStatus
		{
			ArtifactId = artifact.Id,
			ChunkSizeBytes = ChunkSize,
			ReceivedBytes = artifact.SizeBytes,
			ExpectedBytes = artifact.ExpectedSizeBytes,
			NextPartNumber = Parts(artifact).Count + 1,
			Completed = artifact.UploadState == (int)ImportArtifactUploadState.Completed,
			// An aborted or expired upload cannot be continued; the browser starts a new one.
			ErrorCode = artifact.UploadState == (int)ImportArtifactUploadState.InProgress || artifact.UploadState == (int)ImportArtifactUploadState.Completed
				? null
				: "IMP-UPLOAD-EXPIRED"
		};

		private async Task<bool> IsPaidAsync(int departmentId)
		{
			try
			{
				var plan = await _subscriptions.GetCurrentPlanForDepartmentAsync(departmentId, false);
				return plan != null && !plan.IsFree;
			}
			catch (Exception ex)
			{
				// Billing unavailable: the free plan's (smaller) limits apply.
				Logging.LogException(ex);
				return false;
			}
		}

		private static bool IsHex64(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

		private static string Clip(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
