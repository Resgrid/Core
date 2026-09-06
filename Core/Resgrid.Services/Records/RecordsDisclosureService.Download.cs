using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;

namespace Resgrid.Services.Records
{
	public partial class RecordsDisclosureService
	{
		public async Task<RmsRecordAttachment> GetReviewAttachmentAsync(int departmentId, string userId, string requestId, string recordId, string revisionId, string attachmentId, string profile)
		{
			var review = await GetReviewAsync(departmentId, userId, requestId, profile);
			var expected = review.Records.SingleOrDefault(r => r.RecordId == recordId && r.RevisionId == revisionId)?.Attachments.SingleOrDefault(a => a.AttachmentId == attachmentId);
			if (expected == null) return null;
			var file = await _attachments.GetHistoricalByIdForDepartmentAsync(departmentId, attachmentId);
			if (file == null || file.RecordId != recordId || file.Checksum != expected.Checksum || file.Data == null || file.ScanState != (int)RmsAttachmentScanState.Clean || RecordSnapshotSerializer.Checksum(file.Data) != file.Checksum) return null;
			if (file.RequiresRestrictedAccess && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
			await RequireDisclosureAsync(departmentId, userId);
			if (!await CanViewDisclosureRecordAsync(departmentId, userId, recordId, review.Records.Single(r => r.RecordId == recordId).RecordKind)) throw new UnauthorizedAccessException();
			await InTransactionAsync(() => AuditAsync(departmentId, userId, recordId, RmsAccessAuditAction.Read, "Disclosure attachment reviewed", new { requestId, revisionId, attachmentId, file.Checksum }, CancellationToken.None));
			return file;
		}
		public async Task<RmsDisclosureDownload> DownloadAsync(int departmentId, string userId, string productionId, string format)
		{
			if (format != "pdf" && format != "zip" && format != "json") throw new ArgumentException("Choose PDF, ZIP, or JSON.");
			var production = await GetAuthorizedProductionAsync(departmentId, userId, productionId) ?? throw new UnauthorizedAccessException();
			var artifact = JObject.Parse(production.ArtifactJson); var name = "disclosure-" + production.ProductionNumber;
			byte[] bytes; string contentType;
			if (format == "json") { bytes = Encoding.UTF8.GetBytes(production.ArtifactJson); contentType = "application/json"; }
			else
			{
				if ((string)artifact["format"] != "resgrid.disclosure.v2" || (string)artifact["pdf_base64"] == null) throw new InvalidOperationException("This legacy production has a JSON artifact only; its original contents remain available as JSON.");
				var pdf = Convert.FromBase64String((string)artifact["pdf_base64"]);
				if (RecordSnapshotSerializer.Checksum(pdf) != (string)artifact["pdf_checksum"]) throw new InvalidOperationException("The packet PDF failed its integrity check.");
				if (format == "pdf") { bytes = pdf; contentType = "application/pdf"; }
				else
				{
					using var output = new MemoryStream();
					using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
					{
						void Add(string path, byte[] data) { var entry = zip.CreateEntry(path, CompressionLevel.Fastest); entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero); using var stream = entry.Open(); stream.Write(data, 0, data.Length); }
						Add("packet.pdf", pdf); artifact.Remove("pdf_base64"); var index = 0;
						foreach (var file in (artifact["attachments"] as JArray ?? new JArray()).OfType<JObject>())
						{
							var data = Convert.FromBase64String((string)file["data_base64"]);
							if (RecordSnapshotSerializer.Checksum(data) != (string)file["checksum"]) throw new InvalidOperationException("A packet attachment failed its integrity check.");
							var fileName = (string)file["name"] ?? "attachment";
							foreach (var invalid in Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' })) fileName = fileName.Replace(invalid, '_');
							var path = "attachments/" + (++index).ToString("D4") + "-" + fileName;
							Add(path, data); file.Remove("data_base64"); file["packet_path"] = path;
						}
						Add("manifest.json", Encoding.UTF8.GetBytes(artifact.ToString(Formatting.Indented)));
					}
					bytes = output.ToArray(); contentType = "application/zip";
				}
			}
			// Download remains an officer action. Delivery to a requester is recorded separately on release.
			if (await GetAuthorizedProductionAsync(departmentId, userId, productionId) == null) throw new UnauthorizedAccessException();
			await InTransactionAsync(() => AuditAsync(departmentId, userId, null, RmsAccessAuditAction.Export, "Disclosure packet downloaded", new { productionId, production.Checksum, format }, CancellationToken.None));
			return new RmsDisclosureDownload { Data = bytes, ContentType = contentType, FileName = name + "." + format };
		}
	}
}
