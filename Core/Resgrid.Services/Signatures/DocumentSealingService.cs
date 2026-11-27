using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// L3 sealing (e-Signature S1). Renders with NReco, appends the Resgrid audit page, and seals through the resgrid-sealing EU DSS
	/// sidecar with DSS's two-step flow (data to sign → the key provider signs the SHA-256 → sign document), then validates the
	/// result and refuses it unless DSS reports a timestamp. Fails closed: anything not configured or not reachable leaves the
	/// document "Signed, sealing pending" with its input kept (encrypted) for worker 90, which retries with backoff. It never
	/// seals with a local time.
	/// </summary>
	public class DocumentSealingService : IDocumentSealingService
	{
		/// <summary>A worker claim older than this is taken over (the worker that held it stopped).</summary>
		public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(15);

		private readonly ISealedDocumentsRepository _documents;
		private readonly ISealedDocumentInputsRepository _inputs;
		private readonly IDssClient _dss;
		private readonly ISigningKeyProvider _keys;
		private readonly ISealedDocumentStore _store;
		private readonly IPdfProvider _pdf;
		private readonly IEncryptionService _encryption;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDepartmentDataProtectionService _protection;

		/// <summary>
		/// Subjects whose rendering carries a Records revision's or disclosure's values as the requesting member revealed them. For a department
		/// that encrypts new writes (ADP) their sealed copy is never kept: ADP's rule for exports is that a stored copy inherits its source's
		/// protection (RmsExportRuns.Data, RmsDisclosureProductions.ArtifactJson are sealed), and the sealed store keeps plain PDFs that the
		/// unattended re-timestamping and retry steps read without a grant. The member still gets the sealed file; Resgrid keeps its hash.
		/// </summary>
		public static readonly System.Collections.Generic.IReadOnlyList<SignatureSubjectKinds> ProtectedContentKinds = new[] { SignatureSubjectKinds.RecordsDocument, SignatureSubjectKinds.RecordsDisclosure };

		public DocumentSealingService(ISealedDocumentsRepository documents, ISealedDocumentInputsRepository inputs, IDssClient dss, ISigningKeyProvider keys,
			ISealedDocumentStore store, IPdfProvider pdf, IEncryptionService encryption, IEventAggregator eventAggregator, IDepartmentDataProtectionService protection)
		{
			_protection = protection;
			_documents = documents;
			_inputs = inputs;
			_dss = dss;
			_keys = keys;
			_store = store;
			_pdf = pdf;
			_encryption = encryption;
			_eventAggregator = eventAggregator;
		}

		public SealingAvailability GetAvailability()
		{
			return new SealingAvailability
			{
				SidecarConfigured = _dss.IsConfigured,
				TimestampAuthorityConfigured = !string.IsNullOrWhiteSpace(SealingConfig.TsaUrl),
				SigningKeyConfigured = _keys.IsConfigured,
				StoreConfigured = _store.IsConfigured
			};
		}

		public async Task<SealOutcome> SealHtmlAsync(SealHtmlRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.SubjectId) || !SignaturePolicy.IsSha256(request.RevisionSha256) || string.IsNullOrWhiteSpace(request.Html))
				throw new ArgumentException("A department, subject, revision hash and HTML are required.", nameof(request));

			var revision = request.RevisionSha256.ToLowerInvariant();
			if (request.ReuseExisting)
			{
				var reused = await ReuseAsync(request.DepartmentId, request.SubjectKind, request.SubjectId, (int)request.Role, revision, cancellationToken);
				if (reused != null)
					return reused;
			}

			var html = SealingAuditPage.Append(request.Html, request, request.Culture);
			var pdf = _pdf.ConvertHtmlToPdf(html, string.IsNullOrWhiteSpace(request.PageSize) ? "Letter" : request.PageSize);
			var level = SealingPolicy.LevelFor(request.RetentionYears, request.LongTermSubject);
			var existingPending = request.ReuseExisting ? await PendingForAsync(request.DepartmentId, request.SubjectKind, request.SubjectId, (int)request.Role, revision) : null;

			var document = existingPending ?? new SealedDocument
			{
				DepartmentId = request.DepartmentId,
				SubjectKind = (int)request.SubjectKind,
				SubjectId = Truncate(request.SubjectId, 128),
				RevisionId = Truncate(request.RevisionId, 128),
				RevisionSha256 = revision,
				Role = (int)request.Role,
				SignatureRequestId = request.SignatureRequestId,
				Title = Truncate(request.Title, 256),
				FileName = Truncate(SafeFileName(request.FileName), 256),
				Status = (int)SealedDocumentStatuses.Pending,
				PadesLevel = (int)level,
				RetentionYears = request.RetentionYears,
				AuditPageIncluded = true,
				CreatedByUserId = request.RequestedByUserId,
				CreatedOn = DateTime.UtcNow
			};

			return await SealOrQueueAsync(document, pdf, html, null, request.PageSize, existingPending != null, cancellationToken);
		}

		public async Task<SealOutcome> SealPdfAsync(SealPdfRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.SubjectId) || request.Pdf == null || request.Pdf.Length == 0)
				throw new ArgumentException("A department, subject and PDF are required.", nameof(request));
			if (!SealingPolicy.LooksLikePdf(request.Pdf))
				throw new ArgumentException("The document is not a PDF.", nameof(request));

			var revision = SignaturePolicy.IsSha256(request.RevisionSha256) ? request.RevisionSha256.ToLowerInvariant() : SealingPolicy.Sha256(request.Pdf);
			if (await KeepsNoCopyAsync(request.DepartmentId, request.SubjectKind))
				return await SealWithoutCopyAsync(new SealedDocument
				{
					DepartmentId = request.DepartmentId,
					SubjectKind = (int)request.SubjectKind,
					SubjectId = Truncate(request.SubjectId, 128),
					RevisionId = Truncate(request.RevisionId, 128),
					RevisionSha256 = revision,
					Role = (int)request.Role,
					SignatureRequestId = request.SignatureRequestId,
					Title = Truncate(request.Title, 256),
					FileName = Truncate(SafeFileName(request.FileName), 256),
					Status = (int)SealedDocumentStatuses.Pending,
					PadesLevel = (int)SealingPolicy.LevelFor(request.RetentionYears, request.LongTermSubject),
					RetentionYears = request.RetentionYears,
					AuditPageIncluded = false,
					CreatedByUserId = request.RequestedByUserId,
					CreatedOn = DateTime.UtcNow
				}, request.Pdf, cancellationToken);
			if (request.ReuseExisting)
			{
				var reused = await ReuseAsync(request.DepartmentId, request.SubjectKind, request.SubjectId, (int)request.Role, revision, cancellationToken);
				if (reused != null)
					return reused;
			}

			var existingPending = request.ReuseExisting ? await PendingForAsync(request.DepartmentId, request.SubjectKind, request.SubjectId, (int)request.Role, revision) : null;
			var document = existingPending ?? new SealedDocument
			{
				DepartmentId = request.DepartmentId,
				SubjectKind = (int)request.SubjectKind,
				SubjectId = Truncate(request.SubjectId, 128),
				RevisionId = Truncate(request.RevisionId, 128),
				RevisionSha256 = revision,
				Role = (int)request.Role,
				SignatureRequestId = request.SignatureRequestId,
				Title = Truncate(request.Title, 256),
				FileName = Truncate(SafeFileName(request.FileName), 256),
				Status = (int)SealedDocumentStatuses.Pending,
				PadesLevel = (int)SealingPolicy.LevelFor(request.RetentionYears, request.LongTermSubject),
				RetentionYears = request.RetentionYears,
				// Resgrid renders with NReco and has no PDF merge (no PDFsharp/iText in Core, §22.2), so a module's PDF is sealed as given.
				AuditPageIncluded = false,
				CreatedByUserId = request.RequestedByUserId,
				CreatedOn = DateTime.UtcNow
			};

			return await SealOrQueueAsync(document, request.Pdf, null, request.Pdf, null, existingPending != null, cancellationToken);
		}

		public async Task<SealedDocument> StoreAsIsAsync(int departmentId, SignatureSubjectKinds subjectKind, string subjectId, string revisionSha256, int? signatureRequestId,
			SealedDocumentRoles role, string title, string fileName, byte[] bytes, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(subjectId) || bytes == null || bytes.Length == 0)
				return null;
			if (!_store.IsConfigured)
				return null;

			var sha = SealingPolicy.Sha256(bytes);
			var document = new SealedDocument
			{
				DepartmentId = departmentId,
				SubjectKind = (int)subjectKind,
				SubjectId = Truncate(subjectId, 128),
				RevisionSha256 = SignaturePolicy.IsSha256(revisionSha256) ? revisionSha256.ToLowerInvariant() : sha,
				Role = (int)role,
				SignatureRequestId = signatureRequestId,
				Title = Truncate(title, 256),
				FileName = Truncate(SafeFileName(fileName), 256),
				Status = (int)SealedDocumentStatuses.StoredAsIs,
				Sha256 = sha,
				SizeBytes = bytes.LongLength,
				CreatedOn = DateTime.UtcNow
			};
			document = await _documents.InsertAsync(document, cancellationToken, true);
			document.StorageKey = StorageKey(document, sha, role == SealedDocumentRoles.ProviderAuditCertificate && !SealingPolicy.LooksLikePdf(bytes) ? "json" : "pdf");
			await _store.PutAsync(document.StorageKey, bytes, SealingPolicy.LooksLikePdf(bytes) ? "application/pdf" : "application/json", cancellationToken);
			return await _documents.UpdateAsync(document, cancellationToken, true);
		}

		public async Task<SealedDocument> GetLatestAsync(int departmentId, SignatureSubjectKinds subjectKind, string subjectId, string revisionSha256 = null, SealedDocumentRoles role = SealedDocumentRoles.Sealed)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(subjectId))
				return null;
			var rows = await _documents.GetForSubjectAsync(departmentId, (int)subjectKind, subjectId, (int)role, revisionSha256) ?? new List<SealedDocument>();
			return rows.Where(d => d.DepartmentId == departmentId)
				.OrderByDescending(d => d.Status == (int)SealedDocumentStatuses.Sealed || d.Status == (int)SealedDocumentStatuses.StoredAsIs)
				.ThenByDescending(d => d.CreatedOn)
				.FirstOrDefault();
		}

		public async Task<SealedDocument> GetByIdAsync(int departmentId, int sealedDocumentId)
		{
			if (departmentId <= 0 || sealedDocumentId <= 0)
				return null;
			var document = await _documents.GetByIdForDepartmentAsync(departmentId, sealedDocumentId);
			return document != null && document.DepartmentId == departmentId ? document : null;
		}

		public async Task<byte[]> OpenAsync(int departmentId, int sealedDocumentId, CancellationToken cancellationToken = default)
		{
			var document = await GetByIdAsync(departmentId, sealedDocumentId);
			if (document == null || string.IsNullOrWhiteSpace(document.StorageKey) || !_store.IsConfigured)
				return null;
			if (document.Status != (int)SealedDocumentStatuses.Sealed && document.Status != (int)SealedDocumentStatuses.StoredAsIs)
				return null;

			var bytes = await _store.GetAsync(document.StorageKey, cancellationToken);
			// The stored bytes must still be the ones recorded; anything else is not handed out as a Resgrid document.
			return bytes != null && string.Equals(SealingPolicy.Sha256(bytes), document.Sha256, StringComparison.OrdinalIgnoreCase) ? bytes : null;
		}

		public async Task<int> RetryPendingAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default)
		{
			// Nothing is attempted (and no attempt is counted) while sealing is not configured: a department waiting for the operator
			// is never pushed to Failed by the backoff.
			if (!GetAvailability().Ready)
				return 0;

			var sealedCount = 0;
			foreach (var document in await _documents.GetDuePendingAsync(utcNow, utcNow - ClaimLease, Math.Max(1, batchSize)) ?? new List<SealedDocument>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!await _documents.ClaimAsync(document.SealedDocumentId, utcNow - ClaimLease, utcNow, cancellationToken))
					continue;

				try
				{
					var input = await _inputs.GetForDocumentAsync(document.DepartmentId, document.SealedDocumentId);
					var pdf = RenderInput(input);
					if (pdf == null)
					{
						await FailAsync(document, "sealing_input_missing", cancellationToken);
						continue;
					}

					if (await TrySealAsync(document, pdf, cancellationToken))
						sealedCount++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					await _documents.ReleaseAsync(document.SealedDocumentId, CancellationToken.None);
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "A pending seal could not be retried.");
					await RecordFailureAsync(document, ex.Message, cancellationToken);
				}
			}

			return sealedCount;
		}

		public async Task<int> RetimestampDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default)
		{
			if (!GetAvailability().Ready)
				return 0;

			var extended = 0;
			foreach (var document in await _documents.GetDueRetimestampAsync(utcNow, utcNow - ClaimLease, Math.Max(1, batchSize)) ?? new List<SealedDocument>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!await _documents.ClaimAsync(document.SealedDocumentId, utcNow - ClaimLease, utcNow, cancellationToken))
					continue;

				try
				{
					var bytes = await OpenAsync(document.DepartmentId, document.SealedDocumentId, cancellationToken);
					if (bytes == null)
					{
						document.LastError = "retimestamp_source_missing";
						document.RetimestampDueOn = utcNow.AddDays(1);
						document.ClaimedOn = null;
						await _documents.UpdateAsync(document, cancellationToken, true);
						continue;
					}

					var renewed = await _dss.ExtendDocumentAsync(bytes, document.FileName, SealingPolicy.DssName(PadesLevels.BaselineLTA), cancellationToken);
					var timestamp = await RequireTimestampAsync(renewed, document.FileName, cancellationToken);

					// The extended file is a new issued document (the earlier file still verifies as issued); the schedule moves to it.
					var copy = new SealedDocument
					{
						DepartmentId = document.DepartmentId,
						SubjectKind = document.SubjectKind,
						SubjectId = document.SubjectId,
						RevisionId = document.RevisionId,
						RevisionSha256 = document.RevisionSha256,
						Role = document.Role,
						SignatureRequestId = document.SignatureRequestId,
						Title = document.Title,
						FileName = document.FileName,
						Status = (int)SealedDocumentStatuses.Sealed,
						PadesLevel = (int)PadesLevels.BaselineLTA,
						RetentionYears = document.RetentionYears,
						AuditPageIncluded = document.AuditPageIncluded,
						SigningCertThumbprint = document.SigningCertThumbprint,
						CreatedByUserId = document.CreatedByUserId,
						CreatedOn = utcNow
					};
					copy = await _documents.InsertAsync(copy, cancellationToken, true);
					await StoreSealedAsync(copy, renewed, timestamp, utcNow, cancellationToken);

					document.RetimestampDueOn = null;
					document.ClaimedOn = null;
					await _documents.UpdateAsync(document, cancellationToken, true);
					extended++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					await _documents.ReleaseAsync(document.SealedDocumentId, CancellationToken.None);
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "A sealed archive could not be re-timestamped.");
					document.LastError = Truncate(ex.Message, 1000);
					document.RetimestampDueOn = utcNow.AddHours(6);
					document.ClaimedOn = null;
					await _documents.UpdateAsync(document, CancellationToken.None, true);
				}
			}

			return extended;
		}

		public async Task<DocumentVerificationResult> VerifyAsync(int askingDepartmentId, byte[] pdf, CancellationToken cancellationToken = default)
		{
			var result = new DocumentVerificationResult { Sha256 = SealingPolicy.Sha256(pdf) };
			result.Validation = pdf == null || pdf.Length == 0
				? new DssValidationReport { Completed = false, Error = "empty" }
				: await _dss.ValidateAsync(pdf, "uploaded.pdf", cancellationToken);

			var issued = (await _documents.GetBySha256Async(result.Sha256) ?? new List<SealedDocument>())
				.FirstOrDefault(d => d.Status == (int)SealedDocumentStatuses.Sealed || d.Status == (int)SealedDocumentStatuses.StoredAsIs);
			if (issued != null)
			{
				result.IssuedByThisDepartment = issued.DepartmentId == askingDepartmentId && askingDepartmentId > 0;
				// Another department's document only confirms that Resgrid issued it, never what it is about.
				result.IssuedDocument = result.IssuedByThisDepartment ? issued : new SealedDocument
				{
					SealedDocumentId = 0,
					Status = issued.Status,
					Role = issued.Role,
					PadesLevel = issued.PadesLevel,
					Sha256 = issued.Sha256,
					SigningCertThumbprint = issued.SigningCertThumbprint,
					TimestampedOn = issued.TimestampedOn,
					SealedOn = issued.SealedOn,
					CreatedOn = issued.CreatedOn
				};
			}

			return result;
		}

		// ---- internals ----------------------------------------------------------------------------------------------------

		private async Task<SealOutcome> ReuseAsync(int departmentId, SignatureSubjectKinds kind, string subjectId, int role, string revision, CancellationToken cancellationToken)
		{
			var existing = (await _documents.GetForSubjectAsync(departmentId, (int)kind, subjectId, role, revision) ?? new List<SealedDocument>())
				.Where(d => d.DepartmentId == departmentId && d.Status == (int)SealedDocumentStatuses.Sealed)
				.OrderByDescending(d => d.SealedOn ?? d.CreatedOn)
				.FirstOrDefault();
			if (existing == null || !_store.IsConfigured)
				return null;

			try
			{
				var bytes = await OpenAsync(departmentId, existing.SealedDocumentId, cancellationToken);
				return bytes == null ? null : new SealOutcome { Document = existing, Bytes = bytes };
			}
			catch (Exception ex)
			{
				// An unreachable store makes this a fresh seal attempt (which will itself fail closed).
				Logging.LogException(ex, "A sealed copy could not be read for re-download.");
				return null;
			}
		}

		private async Task<SealedDocument> PendingForAsync(int departmentId, SignatureSubjectKinds kind, string subjectId, int role, string revision)
		{
			return (await _documents.GetForSubjectAsync(departmentId, (int)kind, subjectId, role, revision) ?? new List<SealedDocument>())
				.Where(d => d.DepartmentId == departmentId && d.Status == (int)SealedDocumentStatuses.Pending)
				.OrderByDescending(d => d.CreatedOn)
				.FirstOrDefault();
		}

		private async Task<SealOutcome> SealOrQueueAsync(SealedDocument document, byte[] pdf, string html, byte[] pdfInput, string pageSize, bool alreadyQueued, CancellationToken cancellationToken)
		{
			if (pdf == null || pdf.Length == 0)
				throw new InvalidOperationException("sealing_render_failed");

			var availability = GetAvailability();
			if (document.SealedDocumentId <= 0)
				document = await _documents.InsertAsync(document, cancellationToken, true);

			if (availability.Ready)
			{
				try
				{
					var claimed = await _documents.ClaimAsync(document.SealedDocumentId, DateTime.UtcNow - ClaimLease, DateTime.UtcNow, cancellationToken);
					if (claimed && await TrySealAsync(document, pdf, cancellationToken))
					{
						var stored = await OpenAsync(document.DepartmentId, document.SealedDocumentId, cancellationToken);
						return new SealOutcome { Document = document, Bytes = stored ?? pdf };
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "Sealing failed; the document stays pending.");
					await RecordFailureAsync(document, ex.Message, cancellationToken, countAttempt: false);
				}
			}

			if (!alreadyQueued)
				await QueueInputAsync(document, html, pdfInput, pageSize, cancellationToken);

			if (!availability.Ready && document.NextAttemptOn == null)
			{
				document.NextAttemptOn = DateTime.UtcNow.Add(SealingPolicy.Backoff(1));
				document.LastError = availability.MissingPart;
				document.ClaimedOn = null;
				await _documents.UpdateAsync(document, cancellationToken, true);
			}

			return new SealOutcome { Document = document, Bytes = pdf, Reason = availability.Ready ? document.LastError : availability.MissingPart };
		}

		private async Task QueueInputAsync(SealedDocument document, string html, byte[] pdfInput, string pageSize, CancellationToken cancellationToken)
		{
			if (pdfInput != null && pdfInput.Length > SealingPolicy.MaxInputBytes)
			{
				await FailAsync(document, "sealing_input_too_large", cancellationToken);
				return;
			}

			// The input waits encrypted: it can hold a drawn signature or a member's record.
			await _inputs.InsertAsync(new SealedDocumentInput
			{
				SealedDocumentId = document.SealedDocumentId,
				DepartmentId = document.DepartmentId,
				Html = html == null ? null : _encryption.Encrypt(html),
				PdfBase64 = pdfInput == null ? null : _encryption.Encrypt(Convert.ToBase64String(pdfInput)),
				PageSize = Truncate(pageSize, 16),
				CreatedOn = DateTime.UtcNow
			}, cancellationToken, true);
		}

		private byte[] RenderInput(SealedDocumentInput input)
		{
			if (input == null)
				return null;
			if (!string.IsNullOrEmpty(input.PdfBase64))
				return Convert.FromBase64String(_encryption.Decrypt(input.PdfBase64));
			if (!string.IsNullOrEmpty(input.Html))
				return _pdf.ConvertHtmlToPdf(_encryption.Decrypt(input.Html), string.IsNullOrWhiteSpace(input.PageSize) ? "Letter" : input.PageSize);
			return null;
		}

		/// <summary>One sealing attempt on a claimed row: sign remotely, require a timestamp, store, mark sealed.</summary>
		private async Task<bool> TrySealAsync(SealedDocument document, byte[] pdf, CancellationToken cancellationToken)
		{
			var (sealedPdf, timestamp) = await SignAsync(document, pdf, cancellationToken);
			await StoreSealedAsync(document, sealedPdf, timestamp, DateTime.UtcNow, cancellationToken);
			await _inputs.DeleteForDocumentAsync(document.DepartmentId, document.SealedDocumentId, cancellationToken);
			Audit(document, AuditLogTypes.DocumentSealed, true);
			RaiseWorkflow(document, DocumentSealingWorkflowChanges.Sealed);
			return true;
		}

		/// <summary>Signs remotely and requires a timestamp (never a local-time seal); sets the certificate thumbprint on the row.</summary>
		private async Task<(byte[] SealedPdf, DssSignatureResult Timestamp)> SignAsync(SealedDocument document, byte[] pdf, CancellationToken cancellationToken)
		{
			var certificate = await _keys.GetCertificateAsync(cancellationToken);
			var level = SealingPolicy.DssName((PadesLevels)document.PadesLevel);
			var signingDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			var fileName = string.IsNullOrWhiteSpace(document.FileName) ? "document.pdf" : document.FileName;

			var dataToSign = await _dss.GetDataToSignAsync(pdf, fileName, level, certificate, signingDate, cancellationToken);
			var signatureValue = await _keys.SignDigestAsync(SHA256.HashData(dataToSign), cancellationToken);
			var sealedPdf = await _dss.SignDocumentAsync(pdf, fileName, level, certificate, signingDate, signatureValue, cancellationToken);
			var timestamp = await RequireTimestampAsync(sealedPdf, fileName, cancellationToken);

			document.SigningCertThumbprint = Truncate(certificate.Thumbprint, 128);
			return (sealedPdf, timestamp);
		}

		/// <summary>True when the subject's sealed copy must not be kept: a protected-content kind of a department that encrypts new writes.</summary>
		private async Task<bool> KeepsNoCopyAsync(int departmentId, SignatureSubjectKinds kind)
		{
			if (!ProtectedContentKinds.Contains(kind))
				return false;
			if (_protection == null)
				return false;
			try
			{
				return await _protection.ShouldEncryptNewWritesAsync(departmentId);
			}
			catch (Exception ex)
			{
				// Unknown state: keep nothing (a kept plaintext copy is the one direction that cannot be taken back).
				Logging.LogException(ex, $"The protection state of department {departmentId} could not be read; the sealed copy is not kept.");
				return true;
			}
		}

		/// <summary>
		/// ADP: seals now and hands the sealed bytes back without keeping them (no store object, no queued input, no re-timestamp schedule);
		/// the row keeps the hash so the copy still verifies as issued. When sealing is not available now, nothing is kept or queued and the
		/// unsealed rendering is returned.
		/// </summary>
		private async Task<SealOutcome> SealWithoutCopyAsync(SealedDocument document, byte[] pdf, CancellationToken cancellationToken)
		{
			var availability = GetAvailability();
			if (!(availability.SidecarConfigured && availability.TimestampAuthorityConfigured && availability.SigningKeyConfigured))
				return new SealOutcome { Bytes = pdf, Reason = availability.MissingPart };

			try
			{
				var (sealedPdf, timestamp) = await SignAsync(document, pdf, cancellationToken);
				var utcNow = DateTime.UtcNow;
				document.Status = (int)SealedDocumentStatuses.Sealed;
				document.Sha256 = SealingPolicy.Sha256(sealedPdf);
				document.SizeBytes = sealedPdf.LongLength;
				document.TimestampedOn = timestamp?.TimestampTime;
				document.SealedOn = utcNow;
				document.StorageKey = null;
				document.RetimestampDueOn = null;
				document = await _documents.InsertAsync(document, cancellationToken, true);
				Audit(document, AuditLogTypes.DocumentSealed, true);
				return new SealOutcome { Document = document, Bytes = sealedPdf };
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Sealing failed; the protected copy is returned unsealed and nothing is kept.");
				return new SealOutcome { Bytes = pdf, Reason = ErrorCode(ex.Message) };
			}
		}

		/// <summary>
		/// Workflow triggers 218-219 after the document's state is stored (a retry holds the row's claim, so once per document). Records
		/// documents and disclosures raise nothing here (the Records module's own events cover them and they can belong to restricted
		/// records); the raw error text never leaves, only a short code.
		/// </summary>
		private void RaiseWorkflow(SealedDocument document, DocumentSealingWorkflowChanges change)
		{
			if (document == null || !RaisesWorkflow(document.SubjectKind))
				return;

			ContentWorkflowContext.Publish(_eventAggregator, WorkflowEvent(document, change, DateTime.UtcNow));
		}

		/// <summary>Whether a sealed document of this subject kind raises the sealing workflow events (not Records documents or disclosures).</summary>
		public static bool RaisesWorkflow(int subjectKind) =>
			subjectKind != (int)SignatureSubjectKinds.RecordsDocument && subjectKind != (int)SignatureSubjectKinds.RecordsDisclosure;

		public static DocumentSealingWorkflowEvent WorkflowEvent(SealedDocument document, DocumentSealingWorkflowChanges change, DateTime utcNow) => new DocumentSealingWorkflowEvent
		{
			DepartmentId = document.DepartmentId,
			Change = change,
			SealedDocumentId = document.SealedDocumentId,
			SubjectKind = Enum.IsDefined(typeof(SignatureSubjectKinds), document.SubjectKind) ? ((SignatureSubjectKinds)document.SubjectKind).ToString() : null,
			SubjectId = document.SubjectId,
			DocumentTitle = document.Title,
			DocumentRole = Enum.IsDefined(typeof(SealedDocumentRoles), document.Role) ? ((SealedDocumentRoles)document.Role).ToString() : null,
			SignatureRequestId = document.SignatureRequestId,
			Status = Enum.IsDefined(typeof(SealedDocumentStatuses), document.Status) ? ((SealedDocumentStatuses)document.Status).ToString() : null,
			PadesLevel = Enum.IsDefined(typeof(PadesLevels), document.PadesLevel) ? ((PadesLevels)document.PadesLevel).ToString() : null,
			Attempts = document.Attempts,
			ErrorCode = change == DocumentSealingWorkflowChanges.Failed ? ErrorCode(document.LastError) : null,
			SealedOn = document.SealedOn,
			UserId = document.CreatedByUserId,
			OccurredOn = utcNow
		};

		/// <summary>A stored error that is one of the service's own codes ("sealing_no_timestamp") passes; any other text reads "sealing_error".</summary>
		public static string ErrorCode(string error)
		{
			var code = (error ?? string.Empty).Trim();
			return code.Length > 0 && code.Length <= 64 && code.All(c => (c >= 'a' && c <= 'z') || c == '_' || (c >= '0' && c <= '9')) ? code : "sealing_error";
		}

		/// <summary>Validates sealed bytes and returns the timestamp evidence; refuses a result without one (never a local-time seal).</summary>
		private async Task<DssSignatureResult> RequireTimestampAsync(byte[] sealedPdf, string fileName, CancellationToken cancellationToken)
		{
			if (sealedPdf == null || !SealingPolicy.LooksLikePdf(sealedPdf))
				throw new InvalidOperationException("sealing_sidecar_bad_document");

			var report = await _dss.ValidateAsync(sealedPdf, fileName, cancellationToken);
			if (report == null || !report.Completed)
				throw new InvalidOperationException("sealing_validation_unavailable");

			// Resgrid's seal is the newest signature. A self-hosted certificate outside the trust lists is INDETERMINATE (Acrobat's
			// "validity unknown", decision 16b); a failed signature or a missing timestamp is refused.
			var seal = report.Signatures.LastOrDefault();
			if (seal == null || string.Equals(seal.Indication, "TOTAL_FAILED", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("sealing_validation_failed");
			if (!seal.HasTimestamp)
				throw new InvalidOperationException("sealing_no_timestamp");
			return seal;
		}

		private async Task StoreSealedAsync(SealedDocument document, byte[] sealedPdf, DssSignatureResult timestamp, DateTime utcNow, CancellationToken cancellationToken)
		{
			var sha = SealingPolicy.Sha256(sealedPdf);
			document.StorageKey = StorageKey(document, sha, "pdf");
			await _store.PutAsync(document.StorageKey, sealedPdf, "application/pdf", cancellationToken);

			document.Status = (int)SealedDocumentStatuses.Sealed;
			document.Sha256 = sha;
			document.SizeBytes = sealedPdf.LongLength;
			document.TimestampedOn = timestamp?.TimestampTime;
			document.TimestampToken = Truncate(JsonConvert.SerializeObject(new
			{
				tsa = timestamp?.TimestampAuthority,
				time = timestamp?.TimestampTime?.ToString("o", CultureInfo.InvariantCulture),
				format = timestamp?.SignatureFormat,
				indication = timestamp?.Indication
			}), 1000);
			document.SealedOn = utcNow;
			document.RetimestampDueOn = document.PadesLevel == (int)PadesLevels.BaselineLTA ? utcNow.Add(SealingPolicy.RetimestampInterval) : (DateTime?)null;
			document.NextAttemptOn = null;
			document.LastError = null;
			document.ClaimedOn = null;
			await _documents.UpdateAsync(document, cancellationToken, true);
		}

		private async Task RecordFailureAsync(SealedDocument document, string error, CancellationToken cancellationToken, bool countAttempt = true)
		{
			document.Attempts = Math.Max(0, document.Attempts) + 1;
			document.LastError = Truncate(error, 1000);
			document.ClaimedOn = null;
			if (countAttempt && document.Attempts >= SealingPolicy.MaxAttempts)
			{
				document.Status = (int)SealedDocumentStatuses.Failed;
				document.NextAttemptOn = null;
				await _documents.UpdateAsync(document, CancellationToken.None, true);
				Audit(document, AuditLogTypes.DocumentSealingFailed, false);
				RaiseWorkflow(document, DocumentSealingWorkflowChanges.Failed);
				return;
			}

			document.NextAttemptOn = DateTime.UtcNow.Add(SealingPolicy.Backoff(document.Attempts));
			await _documents.UpdateAsync(document, CancellationToken.None, true);
		}

		private async Task FailAsync(SealedDocument document, string error, CancellationToken cancellationToken)
		{
			document.Status = (int)SealedDocumentStatuses.Failed;
			document.LastError = error;
			document.NextAttemptOn = null;
			document.ClaimedOn = null;
			await _documents.UpdateAsync(document, cancellationToken, true);
			Audit(document, AuditLogTypes.DocumentSealingFailed, false);
			RaiseWorkflow(document, DocumentSealingWorkflowChanges.Failed);
		}

		private static string StorageKey(SealedDocument document, string sha, string extension) =>
			string.Join("/", new[]
			{
				(SealingConfig.S3Prefix ?? "sealed").Trim('/'),
				document.DepartmentId.ToString(CultureInfo.InvariantCulture),
				document.SubjectKind.ToString(CultureInfo.InvariantCulture),
				document.SealedDocumentId.ToString(CultureInfo.InvariantCulture) + "-" + sha.Substring(0, 16) + "." + extension
			}.Where(p => !string.IsNullOrEmpty(p)));

		private static string SafeFileName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return "document.pdf";
			var clean = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '-').ToArray());
			return clean.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? clean : clean + ".pdf";
		}

		private void Audit(SealedDocument document, AuditLogTypes type, bool successful)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = document.DepartmentId,
				UserId = document.CreatedByUserId,
				Type = type,
				After = JsonConvert.SerializeObject(new
				{
					document.SealedDocumentId,
					document.SubjectKind,
					document.SubjectId,
					document.RevisionSha256,
					document.Role,
					document.PadesLevel,
					document.Sha256,
					document.SigningCertThumbprint,
					document.TimestampedOn,
					document.Attempts,
					document.LastError
				}),
				Successful = successful,
				ServerName = Environment.MachineName
			});
		}

		private static string Truncate(string value, int max)
		{
			if (string.IsNullOrEmpty(value))
				return value;
			return value.Length > max ? value.Substring(0, max) : value;
		}
	}
}
