using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Bulk packets and bulk assign-for-review (RMS plan section 4.7). A packet is compiled from each record's own
	/// document rendering (pinned revision, provenance footer, layout) so it never becomes a second renderer; the result
	/// is an RmsExportRun like any other department export (30-day retention, ADP-sealed bytes, per-record Export audit)
	/// and optionally rides the scheduled-report email path.
	/// </summary>
	public class RecordsBulkPacketService : IRecordsBulkPacketService
	{
		public const string PacketTemplateKey = "bulk-packet";
		public const int RunRetentionDays = 30;

		private readonly IRecordsDocumentService _documents;
		private readonly IRecordsService _records;
		private readonly IRmsOperationalRecordsRepository _recordRows;
		private readonly IRmsExportRunsRepository _runs;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsProtectionService _protection;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IDepartmentsService _departments;
		private readonly IEmailService _email;
		private readonly IPdfProvider _pdf;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsBulkPacketService(IRecordsDocumentService documents, IRecordsService records, IRmsOperationalRecordsRepository recordRows, IRmsExportRunsRepository runs,
			IRecordsAuthorizationService authorization, IRecordsProtectionService protection, IRmsAccessAuditsRepository audits, IDepartmentsService departments,
			IEmailService email, IPdfProvider pdf, IUnitOfWork unitOfWork)
		{
			_documents = documents;
			_records = records;
			_recordRows = recordRows;
			_runs = runs;
			_authorization = authorization;
			_protection = protection;
			_audits = audits;
			_departments = departments;
			_email = email;
			_pdf = pdf;
			_unitOfWork = unitOfWork;
		}

		public async Task<RecordsBulkResult> BuildPacketAsync(int departmentId, string userId, RecordsBulkPacketRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("An acting user is required.", nameof(userId));
			var ids = (request.RecordIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
			if (ids.Count == 0) throw new ArgumentException("Select at least one record.", nameof(request));
			if (ids.Count > RecordsBulkPacketRequest.MaxRecords) throw new ArgumentException($"A packet compiles at most {RecordsBulkPacketRequest.MaxRecords} records; narrow the selection.", nameof(request));
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ExportRecords))
				throw new UnauthorizedAccessException("Bulk packets require the ExportRecords permission.");
			if (!string.IsNullOrWhiteSpace(request.DeliverToEmail) && !IsPlausibleEmail(request.DeliverToEmail))
				throw new ArgumentException("The delivery address is not a valid email address.", nameof(request));

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var rows = (await _recordRows.GetByIdsAsync(departmentId, ids))?.ToDictionary(r => r.RmsOperationalRecordId, StringComparer.Ordinal) ?? new Dictionary<string, RmsOperationalRecord>();
			var result = new RecordsBulkResult();
			var entries = new List<(RmsOperationalRecord Record, RecordDocument Document, string Html)>();

			foreach (var id in ids)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!rows.TryGetValue(id, out var record) || record.DeletedOn.HasValue || record.PurgedOn.HasValue) { Skip(result, id, "not_found"); continue; }
				if (!await _authorization.CanUserViewRecordAsync(userId, id, departmentId)) { Skip(result, id, "not_visible"); continue; }
				if (string.IsNullOrWhiteSpace(record.CurrentRevisionId)) { Skip(result, id, "no_revision"); continue; }
				RecordDocument document;
				try { document = await _documents.GetAsync(departmentId, userId, id, RmsRecordKind.Operational, record.CurrentRevisionId, exporting: true); }
				catch (UnauthorizedAccessException) { Skip(result, id, "not_authorized"); continue; }
				if (document == null) { Skip(result, id, "no_revision"); continue; }
				entries.Add((record, document, await _documents.RenderHtmlAsync(departmentId, userId, document)));
			}

			if (entries.Count == 0)
				throw new InvalidOperationException("Nothing in the selection could be compiled: " + string.Join(", ", result.Skips.Select(s => s.RecordId + " (" + s.Reason + ")")));

			var now = DateTime.UtcNow;
			var title = string.IsNullOrWhiteSpace(request.Title) ? "Records packet" : request.Title.Trim();
			byte[] bytes;
			string fileName, contentType;
			if (request.Mode == RecordsBulkPacketMode.Bundle)
			{
				bytes = BuildBundle(entries, title, department, now, userId);
				fileName = SafeFileName(title) + "-" + now.ToString("yyyyMMdd-HHmm") + ".zip";
				contentType = "application/zip";
			}
			else
			{
				bytes = _pdf.ConvertHtmlToPdf(CompiledHtml(entries, title, department, now, userId), "Letter");
				if (bytes == null || bytes.Length < 4) throw new InvalidOperationException("The PDF provider did not produce a document.");
				fileName = SafeFileName(title) + "-" + now.ToString("yyyyMMdd-HHmm") + ".pdf";
				contentType = "application/pdf";
			}

			var run = new RmsExportRun
			{
				RmsExportRunId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(),
				TemplateId = PacketTemplateKey, TemplateKey = PacketTemplateKey, Trigger = (int)RmsExportTrigger.Bulk,
				RecordCount = entries.Count, FileName = fileName, ContentType = contentType, ByteSize = bytes.LongLength,
				Checksum = RecordSnapshotSerializer.Checksum(bytes), Data = bytes,
				Redacted = entries.Any(e => e.Document.WithheldFields.Count > 0),
				RedactedFieldsJson = entries.Any(e => e.Document.WithheldFields.Count > 0) ? JsonConvert.SerializeObject(new { withheld_fields = entries.SelectMany(e => e.Document.WithheldFields).Distinct().OrderBy(f => f).ToList() }) : null,
				GeneratedOn = now, GeneratedByUserId = userId, ExpiresOn = now.AddDays(RunRetentionDays)
			};

			// Stored bytes are sealed under ADP (RmsExportRuns.Data); the caller keeps the plaintext run for the download.
			var stored = JsonConvert.DeserializeObject<RmsExportRun>(JsonConvert.SerializeObject(run));
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _protection.ProtectExportRunAsync(departmentId, stored, userId, cancellationToken);
				await _runs.InsertAsync(stored, cancellationToken, true);
				foreach (var entry in entries)
					await _audits.InsertAsync(new RmsAccessAudit
					{
						DepartmentId = departmentId, RecordId = entry.Record.RmsOperationalRecordId, RevisionId = entry.Document.RevisionId, Action = (int)RmsAccessAuditAction.Export, ActorUserId = userId,
						Purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "Bulk packet " + title : request.Purpose.Trim(), OriginClient = (int)request.OriginClient, Successful = true, OccurredOn = now,
						DetailJson = JsonConvert.SerializeObject(new { run.RmsExportRunId, run.Checksum, mode = request.Mode.ToString(), records = entries.Count })
					}, cancellationToken, true);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }

			run.IsProtected = stored.IsProtected;
			run.ProtectedCatalogVersion = stored.ProtectedCatalogVersion;
			result.Processed = entries.Count;
			result.Run = run;

			if (!string.IsNullOrWhiteSpace(request.DeliverToEmail))
			{
				// Same path the scheduled PDF reports take (ReportDeliveryLogic): one attachment, department-branded mail.
				result.Delivered = await _email.SendReportDeliveryEmail(new EmailNotification
				{
					To = request.DeliverToEmail.Trim(),
					Subject = $"Resgrid Records packet: {title} ({entries.Count} record(s))",
					Body = $"The Records packet \"{title}\" compiled {entries.Count} record(s) for {department?.Name}. Checksum {run.Checksum}. The packet stays downloadable for {RunRetentionDays} days.",
					AttachmentName = fileName,
					AttachmentData = bytes
				});
			}
			return result;
		}

		public async Task<RecordsBulkResult> AssignForReviewAsync(int departmentId, string userId, RecordsBulkAssignRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			if (string.IsNullOrWhiteSpace(request.ReviewerUserId)) throw new ArgumentException("A reviewer is required.", nameof(request));
			var ids = (request.RecordIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal).ToList();
			if (ids.Count == 0) throw new ArgumentException("Select at least one record.", nameof(request));
			if (ids.Count > RecordsBulkPacketRequest.MaxRecords) throw new ArgumentException($"Assign at most {RecordsBulkPacketRequest.MaxRecords} records at a time.", nameof(request));
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ReviewRecords))
				throw new UnauthorizedAccessException("Bulk assign-for-review requires the ReviewRecords permission.");
			if (!await _authorization.IsActiveMemberAsync(request.ReviewerUserId, departmentId))
				throw new ArgumentException("The reviewer is not an active member of this department.", nameof(request));

			var result = new RecordsBulkResult();
			foreach (var id in ids)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					await _records.AssignReviewerAsync(departmentId, userId, id, request.ReviewerUserId, request.Reason, cancellationToken);
					result.Processed++;
				}
				catch (RecordTransitionException) { Skip(result, id, "not_awaiting_review"); }
				catch (UnauthorizedAccessException) { Skip(result, id, "not_visible"); }
				catch (ArgumentException) { Skip(result, id, "not_found"); }
			}
			return result;
		}

		public async Task<RmsExportRun> GetPacketAsync(int departmentId, string userId, string runId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(runId)) return null;
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ExportRecords))
				throw new UnauthorizedAccessException("Bulk packets require the ExportRecords permission.");
			var run = await _runs.GetWithDataAsync(departmentId, runId);
			if (run == null || run.DeletedOn.HasValue || run.ExpiresOn <= DateTime.UtcNow || !string.Equals(run.TemplateKey, PacketTemplateKey, StringComparison.Ordinal))
				return null;
			(await _protection.RevealExportRunsAsync(departmentId, new[] { run }, true, cancellationToken)).RequireRevealed("packet download");
			await _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId, Action = (int)RmsAccessAuditAction.Export, ActorUserId = userId, Purpose = "Bulk packet download", OriginClient = (int)RmsOriginClient.Web,
				Successful = true, OccurredOn = DateTime.UtcNow, DetailJson = JsonConvert.SerializeObject(new { run.RmsExportRunId, run.Checksum, run.RecordCount })
			}, cancellationToken);
			return run;
		}

		private static void Skip(RecordsBulkResult result, string recordId, string reason)
		{
			result.Skipped++;
			result.Skips.Add(new RecordsBulkSkip { RecordId = recordId, Reason = reason });
		}

		private static bool IsPlausibleEmail(string value)
		{
			try { return new System.Net.Mail.MailAddress(value.Trim()).Address.Length > 0; } catch (FormatException) { return false; }
		}

		internal static string CompiledHtml(List<(RmsOperationalRecord Record, RecordDocument Document, string Html)> entries, string title, Department department, DateTime now, string userId)
		{
			var html = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>").Append(E(title)).Append("</title><style>body{font:12px Arial,sans-serif;color:#142235}h1{font-size:20px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccd;padding:4px 6px;text-align:left;font-size:11px}th{background:#eef1f5}.packet-record{page-break-before:always}.packet-foot{font-size:10px;color:#556;margin-top:12px}</style></head><body>");
			html.Append("<h1>").Append(E(title)).Append("</h1><p>").Append(E(department?.Name ?? string.Empty)).Append(" · compiled ").Append(E(now.ToString("u"))).Append(" · ").Append(entries.Count).Append(" record(s)</p>");
			html.Append("<h2>Manifest</h2><table><thead><tr><th>#</th><th>Record</th><th>Definition</th><th>Revision</th><th>Finalized</th><th>Checksum</th></tr></thead><tbody>");
			for (var i = 0; i < entries.Count; i++)
			{
				var (record, document, _) = entries[i];
				html.Append("<tr><td>").Append(i + 1).Append("</td><td>").Append(E(document.RecordNumber)).Append("</td><td>").Append(E(record.DefinitionKey)).Append(" v").Append(record.DefinitionVersion)
					.Append("</td><td>").Append(document.RevisionNumber).Append("</td><td>").Append(E(document.FinalizedOn.ToString("u"))).Append("</td><td>").Append(E(document.OriginalChecksum)).Append("</td></tr>");
			}
			html.Append("</tbody></table><p class=\"packet-foot\">Bulk packet · every record renders from its pinned revision with its own provenance footer; the manifest checksums are the revision checksums.</p>");
			for (var i = 0; i < entries.Count; i++)
			{
				html.Append("<div class=\"packet-record\"><p class=\"packet-foot\">Packet item ").Append(i + 1).Append(" of ").Append(entries.Count).Append(" · ").Append(E(entries[i].Document.RecordNumber)).Append("</p>");
				html.Append(BodyOf(entries[i].Html)).Append("</div>");
			}
			html.Append("</body></html>");
			return html.ToString();
		}

		private byte[] BuildBundle(List<(RmsOperationalRecord Record, RecordDocument Document, string Html)> entries, string title, Department department, DateTime now, string userId)
		{
			using var stream = new MemoryStream();
			using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
			{
				var manifest = new List<object>();
				for (var i = 0; i < entries.Count; i++)
				{
					var (record, document, html) = entries[i];
					var pdf = _pdf.ConvertHtmlToPdf(html, "Letter");
					if (pdf == null || pdf.Length < 4) throw new InvalidOperationException("The PDF provider did not produce a document.");
					var name = (i + 1).ToString("D3") + "-" + SafeFileName(document.RecordNumber ?? record.RmsOperationalRecordId) + ".pdf";
					var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
					using (var target = entry.Open()) target.Write(pdf, 0, pdf.Length);
					manifest.Add(new { index = i + 1, file = name, record_number = document.RecordNumber, definition_key = record.DefinitionKey, definition_version = record.DefinitionVersion, revision = document.RevisionNumber, finalized_on = document.FinalizedOn, checksum = document.OriginalChecksum, pdf_checksum = RecordSnapshotSerializer.Checksum(pdf) });
				}
				var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
				using var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false));
				writer.Write(JsonConvert.SerializeObject(new { title, department = department?.Name, compiled_on = now, compiled_by = userId, records = manifest }, Formatting.Indented));
			}
			return stream.ToArray();
		}

		/// <summary>The inner body of a rendered record document, so the packet keeps one html/head.</summary>
		internal static string BodyOf(string html)
		{
			if (string.IsNullOrEmpty(html)) return string.Empty;
			var start = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
			if (start < 0) return html;
			start = html.IndexOf('>', start);
			var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
			return start < 0 || end < 0 || end <= start ? html : html.Substring(start + 1, end - start - 1);
		}

		private static string SafeFileName(string value)
		{
			var safe = new string((value ?? "packet").Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray()).Trim('-');
			return string.IsNullOrEmpty(safe) ? "packet" : (safe.Length > 60 ? safe.Substring(0, 60) : safe);
		}

		private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
	}
}
