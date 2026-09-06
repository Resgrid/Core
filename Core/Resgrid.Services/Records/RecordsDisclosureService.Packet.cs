using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	public partial class RecordsDisclosureService
	{
		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRmsIncidentAnalysesRepository _analyses;
		private readonly IRecordsDocumentService _documents;
		private readonly IRmsRecordAttachmentsRepository _attachments;
		private readonly IPdfProvider _pdf;
		private readonly IRecordAttachmentScanner _scanner;
		private const int PacketRecordLimit = 64;
		private const int PacketByteLimit = 64 * 1024 * 1024;

		public async Task<RmsDisclosureScopePreview> PreviewScopeAsync(int departmentId, string userId, string requestId, int take = 200)
		{
			await RequireDisclosureAsync(departmentId, userId);
			var request = await LoadAsync(departmentId, requestId);
			var scope = ParseScope(request.ScopeQueryJson); var result = new RmsDisclosureScopePreview();
			if (scope == null) return result;
			var limit = Math.Clamp(take, 1, 1000);
			// Match the official revision's facts, never mutable amendment dates or Call associations.
			// The bounded scan fails visibly if it cannot account for the complete scope.
			async Task Add(string id, string number, string definition, string revisionId, JObject header, bool deleted, RmsRecordKind kind = RmsRecordKind.Operational)
			{
				if (deleted || scope.DefinitionKey != null && scope.DefinitionKey != definition) return;
				if (revisionId != null)
				{
					var revision = await _revisions.GetByIdForDepartmentAsync(departmentId, revisionId);
					if (revision == null || revision.RecordId != id || revision.RecordKind != (int)kind || RecordSnapshotSerializer.Checksum(revision.SnapshotJson) != revision.Checksum) throw new InvalidOperationException("A disclosure source failed its revision integrity check.");
					var saved = JObject.Parse(revision.SnapshotJson); header = kind != RmsRecordKind.Operational ? (JObject)saved["Report"] ?? header : saved;
				}
				var occurred = (DateTime?)new[] { header?["StartedOn"], header?["CallCreatedOn"], header?["CreatedOn"] }.FirstOrDefault(t => t != null && t.Type != JTokenType.Null);
				if (scope.Year.HasValue && occurred?.Year != scope.Year.Value || scope.CallId.HasValue && (int?)header?["CallId"] != scope.CallId || scope.StationGroupId.HasValue && (int?)header?["StationGroupId"] != scope.StationGroupId) return;
				if (!await CanViewDisclosureRecordAsync(departmentId, userId, id, kind)) { result.WithheldWholeRecordCount++; return; }
				result.MatchedCount++;
				if (result.Items.Count >= limit) { result.Truncated = true; return; }
				result.Items.Add(new RmsDisclosureScopeItem { RecordId = id, RecordKind = kind, RecordNumber = number, DefinitionKey = definition,
					Summary = (string)header?["DisplaySummary"] ?? (string)header?["Details"]?["Type"], OccurredOn = occurred, CurrentRevisionId = revisionId,
					Producible = revisionId != null, NotProducibleReason = revisionId == null ? "Automatic production requires a saved revision. Record how unfinished records will be reviewed separately." : null });
				if (revisionId != null) result.ProducibleCount++;
			}
			if (scope.DefinitionKey != RmsDefinitionKeys.NerisIncidentReport)
				for (var skip = 0; skip < 10000; skip += 250)
				{
					var page = (await _records.GetByDepartmentAndStatesAsync(departmentId, scope.States, null, skip, 250))?.ToList() ?? new List<RmsOperationalRecord>();
					foreach (var r in page) await Add(r.RmsOperationalRecordId, r.RecordNumber ?? r.DraftReference, r.DefinitionKey, r.CurrentRevisionId, JObject.FromObject(r), r.PurgedOn.HasValue || r.DeletedOn.HasValue);
					if (page.Count < 250 || result.Truncated) break;
					if (skip == 9750) result.Truncated = true;
				}
			if (scope.DefinitionKey == null || scope.DefinitionKey == RmsDefinitionKeys.NerisIncidentReport)
				for (var skip = 0; skip < 10000; skip += 250)
				{
					var page = (await _reports.QueryAsync(departmentId, new RmsIncidentReportQuery { States = scope.States, Skip = skip, Take = 250 }))?.ToList() ?? new List<RmsIncidentReport>();
					foreach (var r in page)
					{
						var deleted = r.PurgedOn.HasValue || r.DeletedOn.HasValue;
						await Add(r.RmsIncidentReportId, r.RecordNumber ?? r.DraftReference, RmsDefinitionKeys.NerisIncidentReport, r.CurrentRevisionId, JObject.FromObject(r), deleted, RmsRecordKind.IncidentReport);
						if (deleted) continue;
						var analysis = await _analyses.GetForReportAsync(departmentId, r.RmsIncidentReportId);
						if (analysis == null || analysis.DeletedOn.HasValue) continue;
						var parentHeader = JObject.FromObject(r);
						if (r.CurrentRevisionId != null) parentHeader = (JObject)JObject.Parse((await _revisions.GetByIdForDepartmentAsync(departmentId, r.CurrentRevisionId)).SnapshotJson)["Report"] ?? parentHeader;
						await Add(analysis.RmsIncidentAnalysisId, (r.RecordNumber ?? r.DraftReference) + " · analysis", RmsDefinitionKeys.NerisIncidentReport, analysis.CurrentRevisionId, parentHeader, false, RmsRecordKind.IncidentAnalysis);
					}
					if (page.Count < 250 || result.Truncated) break;
					if (skip == 9750) result.Truncated = true;
				}
			await RequireDisclosureAsync(departmentId, userId);
			foreach (var item in result.Items) if (!await CanViewDisclosureRecordAsync(departmentId, userId, item.RecordId, item.RecordKind)) throw new UnauthorizedAccessException("Record access changed during scope review.");
			return result;
		}

		public async Task<RmsDisclosureReview> GetReviewAsync(int departmentId, string userId, string requestId, string redactionProfile = null)
		{
			await RequireDisclosureAsync(departmentId, userId);
			var restricted = await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords);
			var request = await LoadAsync(departmentId, requestId); RequireOpen(request);
			var profile = Profile(redactionProfile, request); var preview = await PreviewScopeAsync(departmentId, userId, requestId, 1000);
			if (preview.Truncated || preview.ProducibleCount > PacketRecordLimit) throw new InvalidOperationException("The disclosure scope exceeds the packet limit. Narrow the scope or track supplemental requests; no partial packet was created.");
			var review = new RmsDisclosureReview { RequestId = requestId, Profile = profile, ScopeChecksum = ScopeChecksum(request, preview) };
			var visibilityRequired=0;
			foreach (var item in preview.Items.Where(i => i.Producible))
			{
				var doc = await _documents.GetAsync(departmentId, userId, item.RecordId, item.RecordKind, item.CurrentRevisionId) ?? throw new InvalidOperationException("A reviewed revision is unavailable.");
				var hidden = new List<string>(doc.WithheldFields); var content = PrepareDisclosure(doc, profile, hidden);
				visibilityRequired=Math.Max(visibilityRequired, RequiredUdfVisibility(content));
				review.Records.Add(new RmsDisclosureRecordReview { RecordId = item.RecordId, RecordKind = item.RecordKind, RecordNumber = item.RecordNumber, RevisionId = doc.RevisionId,
					RevisionChecksum = doc.OriginalChecksum, ContentChecksum = RecordSnapshotSerializer.Checksum(content.ToString(Formatting.None)), AutomaticWithholds = hidden.Distinct().ToList(), Fields = DisclosureContentPolicy.Fields(content).Where(f => !f.Path.StartsWith("/Attachments/", StringComparison.Ordinal)).ToList(),
					Attachments = AttachmentManifest(content).Select(a => new RmsDisclosureAttachmentDecision { AttachmentId = (string)a["RmsRecordAttachmentId"], FileName = (string)a["FileName"], Checksum = (string)a["Checksum"], Metadata = DisclosureContentPolicy.Fields(a) }).ToList() });
			}
			var finalPreview = await PreviewScopeAsync(departmentId, userId, requestId, 1000);
			if (ScopeChecksum(await LoadAsync(departmentId, requestId), finalPreview) != review.ScopeChecksum) throw new InvalidOperationException("The disclosure scope changed during review. Reload it.");
			if (restricted != await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException("Restricted access changed during review.");
			await RequireDisclosureAsync(departmentId, userId);
			if (visibilityRequired>0 && await _udf.GetVisibilityLevelAsync(departmentId,userId)<visibilityRequired) throw new UnauthorizedAccessException("Custom-field access changed during review.");
			return review;
		}

		public async Task<RmsDisclosureProduction> ProduceAsync(int departmentId, string userId, string requestId, string redactionProfile = null, CancellationToken cancellationToken = default, RmsDisclosureReview review = null)
		{
			await RequireDisclosureAsync(departmentId, userId);
			if (review?.Reviewed != true || string.IsNullOrWhiteSpace(review.Authority) || string.IsNullOrWhiteSpace(review.Basis)) throw new ArgumentException("Complete the officer review and record the applicable authority and disclosure basis before producing a packet.");
			var request = await LoadAsync(departmentId, requestId); RequireOpen(request);
			var profile = Profile(redactionProfile, request);
			var current = await GetReviewAsync(departmentId, userId, requestId, profile);
			if (review.RequestId != requestId || review.Profile != profile || review.ScopeChecksum != current.ScopeChecksum) throw new InvalidOperationException("The scope or access changed. Reload the review.");
			var preview = await PreviewScopeAsync(departmentId, userId, requestId, 1000);
			if ((preview.WithheldWholeRecordCount > 0 || preview.Items.Any(i => !i.Producible)) && string.IsNullOrWhiteSpace(review.UnresolvedScopeHandling)) throw new ArgumentException("Record how unfinished or inaccessible scope items will be resolved; they cannot be silently omitted.");
			if (current.Records.Count == 0) throw new InvalidOperationException("Nothing in scope has a saved revision to produce from.");
			if (review.Records == null || review.Records.Count != current.Records.Count || review.Records.Select(r => r.RecordId).Distinct().Count() != review.Records.Count) throw new ArgumentException("Review every record in scope exactly once.");
			var withheld = new List<RmsRedactionEntry>(); var produced = new JArray(); var documents = new JArray(); var files = new JArray();
			long bytesTotal = 0; var visibilityRequired=0; var restricted = profile == RmsRedactionProfiles.FullDisclosure && await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords);
			foreach (var expected in current.Records)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var decision = review.Records.SingleOrDefault(r => r.RecordId == expected.RecordId && r.RecordKind == expected.RecordKind);
				if (decision == null || decision.RevisionId != expected.RevisionId || decision.RevisionChecksum != expected.RevisionChecksum || decision.ContentChecksum != expected.ContentChecksum) throw new InvalidOperationException("A reviewed record or access changed. Reload the review.");
				var doc = await _documents.GetAsync(departmentId, userId, expected.RecordId, expected.RecordKind, expected.RevisionId) ?? throw new UnauthorizedAccessException();
				var hidden = new List<string>(doc.WithheldFields); var content = PrepareDisclosure(doc, profile, hidden);
				if (RecordSnapshotSerializer.Checksum(content.ToString(Formatting.None)) != expected.ContentChecksum) throw new UnauthorizedAccessException("Record access changed during production.");
				if (decision.WithholdWhole)
				{
					RequireBasis(decision.Authority, decision.Basis);
					withheld.Add(new RmsRedactionEntry { RecordId = expected.RecordId, Section = "Record", Field = "*", Authority = decision.Authority.Trim(), Basis = decision.Basis.Trim() });
					content = new JObject { ["Withheld"] = true };
				}
				else
				{
					foreach (var field in hidden.Distinct()) withheld.Add(new RmsRedactionEntry { RecordId = expected.RecordId, Section = "Access/profile", Field = field, Authority = review.Authority.Trim(), Basis = review.Basis.Trim() });
					visibilityRequired=Math.Max(visibilityRequired, RequiredUdfVisibility(content));
					var manifest = AttachmentManifest(content).ToList();
					if (decision.Attachments == null || decision.Attachments.Count != manifest.Count || decision.Attachments.Select(a => a.AttachmentId).Distinct().Count() != manifest.Count) throw new ArgumentException("Review every attachment exactly once.");
					foreach (var metadata in manifest)
					{
						var id = (string)metadata["RmsRecordAttachmentId"]; var fileDecision = decision.Attachments.SingleOrDefault(a => a.AttachmentId == id);
						if (fileDecision?.Reviewed != true || fileDecision.Checksum != (string)metadata["Checksum"]) throw new ArgumentException("Review the exact attachment contents before including or withholding it.");
						if (fileDecision.Derivative != null && !fileDecision.Include) throw new ArgumentException("Select include to release a reviewed redacted replacement.");
						if (!fileDecision.Include)
						{
							RequireBasis(fileDecision.Authority, fileDecision.Basis);
							withheld.Add(new RmsRedactionEntry { RecordId = expected.RecordId, Section = "Attachment", Field = id, Authority = fileDecision.Authority.Trim(), Basis = fileDecision.Basis.Trim() });
							metadata.Replace(new JObject { ["Withheld"] = true }); continue;
						}
						var file = await _attachments.GetHistoricalByIdForDepartmentAsync(departmentId, id);
						if (file == null || file.RecordId != expected.RecordId || file.Checksum != fileDecision.Checksum || file.ScanState != (int)RmsAttachmentScanState.Clean || file.Data == null || RecordSnapshotSerializer.Checksum(file.Data) != file.Checksum) throw new InvalidOperationException("A reviewed attachment is unavailable, changed, or has not passed scanning.");
						if (file.RequiresRestrictedAccess && !restricted) throw new UnauthorizedAccessException();
						byte[] releasedBytes = file.Data; var releasedName = (string)metadata["FileName"]; var releasedType = (string)metadata["ContentType"]; var releasedChecksum = file.Checksum;
						if (fileDecision.Derivative != null)
						{
							RequireBasis(fileDecision.Authority, fileDecision.Basis);
							var derivative = fileDecision.Derivative;
							if (derivative.Data == null || string.IsNullOrWhiteSpace(derivative.Checksum) || RecordSnapshotSerializer.Checksum(derivative.Data) != derivative.Checksum) throw new ArgumentException("The reviewed replacement file checksum does not match.");
							var clean = RecordAttachmentHygiene.Sanitize(derivative.FileName, derivative.ContentType, derivative.Data);
							var scan = await _scanner.ScanAsync(clean.FileName, clean.ContentType, clean.Data, cancellationToken);
							if (scan?.State != RmsAttachmentScanState.Clean) throw new ArgumentException("The replacement file must pass scanning before production.");
							releasedBytes = clean.Data; releasedName = clean.FileName; releasedType = clean.ContentType; releasedChecksum = RecordSnapshotSerializer.Checksum(clean.Data);
							// Replace original metadata as well; filename/description/author can contain withheld information.
							metadata.Replace(JObject.FromObject(new { SourceAttachmentId = id, SourceChecksum = file.Checksum, FileName = releasedName, ContentType = releasedType, Checksum = releasedChecksum, ByteSize = clean.Data.LongLength, RedactedReplacement = true, ReviewedInputChecksum = derivative.Checksum, ReviewedByUserId = userId }));
							withheld.Add(new RmsRedactionEntry { RecordId = expected.RecordId, Section = "Attachment replacement", Field = id, Authority = fileDecision.Authority.Trim(), Basis = fileDecision.Basis.Trim() });
						}
						bytesTotal += releasedBytes.LongLength; if (bytesTotal > PacketByteLimit) throw new InvalidOperationException("The packet exceeds the 64 MB content limit. Track a supplemental production.");
						files.Add(JObject.FromObject(new { record_id = expected.RecordId, attachment_id = id, name = releasedName, checksum = releasedChecksum, source_checksum = file.Checksum, redacted_replacement = fileDecision.Derivative != null, content_type = releasedType, data_base64 = Convert.ToBase64String(releasedBytes) }));
					}
					if ((decision.Decisions ?? new List<RmsDisclosureFieldDecision>()).Any(d => d.Withhold && (d.Path == "/Attachments" || d.Path?.StartsWith("/Attachments/", StringComparison.Ordinal) == true))) throw new ArgumentException("Use the attachment decision to include or withhold a complete file and its metadata.");
					DisclosureContentPolicy.Apply(content, expected.RecordId, decision.Decisions, withheld);
				}
				produced.Add(JObject.FromObject(new { record_id = expected.RecordId, record_kind = (int)expected.RecordKind, record_number = expected.RecordNumber, revision_id = doc.RevisionId, revision_number = doc.RevisionNumber, revision_checksum = doc.OriginalChecksum }));
				documents.Add(new JObject { ["record_id"] = expected.RecordId, ["content"] = content });
			}
			var now = DateTime.UtcNow;
			var pdf = _pdf.ConvertHtmlToPdf(PacketHtml(request, produced, documents, withheld, now), "Letter");
			if (pdf == null || pdf.Length < 4 || Encoding.ASCII.GetString(pdf, 0, 4) != "%PDF") throw new InvalidOperationException("The PDF provider did not produce a valid packet.");
			var artifact = new JObject { ["format"] = "resgrid.disclosure.v2", ["request_number"] = request.RequestNumber, ["jurisdiction_profile"] = request.JurisdictionProfile,
				["redaction_profile"] = profile, ["restricted_content_included"] = restricted, ["produced_on"] = now,
				["udf_visibility_required"] = visibilityRequired,
				["authority"] = review.Authority.Trim(), ["basis"] = review.Basis.Trim(), ["unresolved_scope_handling"] = review.UnresolvedScopeHandling,
				["scope_fully_resolved"] = preview.WithheldWholeRecordCount == 0 && preview.Items.All(i => i.Producible),
				["manifest"] = new JArray(produced.Select((p, i) => new JObject { ["record_order"] = i + 1, ["record"] = p.DeepClone() })), ["documents"] = documents, ["attachments"] = files,
				["redactions"] = JArray.FromObject(withheld), ["pdf_checksum"] = RecordSnapshotSerializer.Checksum(pdf), ["pdf_base64"] = Convert.ToBase64String(pdf) };
			var json = artifact.ToString(Formatting.None); if (Encoding.UTF8.GetByteCount(json) > PacketByteLimit * 2) throw new InvalidOperationException("The packet exceeds the stored artifact limit.");
			var production = new RmsDisclosureProduction { RmsDisclosureProductionId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), DisclosureRequestId = requestId,
				RedactionProfile = profile, ProducedSetJson = produced.ToString(Formatting.None), ArtifactJson = json, Checksum = RecordSnapshotSerializer.Checksum(json), ByteSize = Encoding.UTF8.GetByteCount(json),
				RecordCount = produced.Count, WithheldFieldsJson = JsonConvert.SerializeObject(withheld), WithheldFieldCount = withheld.Count, PreparedByUserId = userId, PreparedOn = now, CreatedOn = now, ModifiedOn = now, RowVersion = 1 };
			await InTransactionAsync(async () =>
			{
				await RequireDisclosureAsync(departmentId, userId);
				if (restricted && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords)) throw new UnauthorizedAccessException();
				var finalReview = await GetReviewAsync(departmentId, userId, requestId, profile);
				if (finalReview.ScopeChecksum != current.ScopeChecksum || finalReview.Records.Any(r => !current.Records.Any(c => c.RecordId == r.RecordId && c.RevisionId == r.RevisionId && c.ContentChecksum == r.ContentChecksum))) throw new InvalidOperationException("The scope or access changed during production. Reload the review.");
				await GuardRequestAsync(request, request.RowVersion, cancellationToken);
				production.ProductionNumber = await _productions.GetMaxProductionNumberAsync(departmentId, requestId) + 1;
				await _productions.InsertAsync(production, cancellationToken, true);
				request.State = (int)RmsDisclosureState.Produced; request.ModifiedOn = now; request.ModifiedByUserId = userId; request.RowVersion++;
				await _requests.UpdateAsync(request, cancellationToken, true);
				foreach (var record in current.Records) await AuditAsync(departmentId, userId, record.RecordId, RmsAccessAuditAction.Export, "Disclosure production reviewed", new { production.RmsDisclosureProductionId, production.Checksum }, cancellationToken);
			});
			return production;
		}

		private async Task RequireDisclosureAsync(int departmentId, string userId)
		{ if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordDisclosures)) throw new UnauthorizedAccessException(); }
		private static int RequiredUdfVisibility(JObject content) => ((content["CustomFields"] as JObject)?["Fields"] as JArray ?? new JArray()).OfType<JObject>().Select(f=>(int?)(f["Field"] as JObject)?["Visibility"] ?? 3).DefaultIfEmpty(0).Max();
		private async Task<bool> CanViewDisclosureRecordAsync(int departmentId, string userId, string id, RmsRecordKind kind)
		{
			if (kind == RmsRecordKind.IncidentAnalysis)
			{
				var analysis = await _analyses.GetByIdForDepartmentAsync(departmentId, id);
				if (analysis == null || analysis.DeletedOn.HasValue) return false;
				id = analysis.IncidentReportId;
			}
			return await _authorization.CanUserViewRecordAsync(userId, id, departmentId);
		}
		private static void RequireBasis(string authority, string basis)
		{ if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(basis)) throw new ArgumentException("Record the applicable authority and case-specific reason for withholding."); }
		private static string Profile(string value, RmsDisclosureRequest request)
		{
			var profile = Blank(value) ?? request.RedactionProfile ?? RmsRedactionProfiles.Standard;
			if (profile != RmsRedactionProfiles.Standard && profile != RmsRedactionProfiles.FullDisclosure && profile != RmsRedactionProfiles.NoPersonalIdentifiers) throw new ArgumentException("Unknown redaction profile.");
			return profile;
		}
		private static string ScopeChecksum(RmsDisclosureRequest request, RmsDisclosureScopePreview preview) => RecordSnapshotSerializer.Checksum(JsonConvert.SerializeObject(new { request.ScopeQueryJson, request.RowVersion, preview.WithheldWholeRecordCount, Items = preview.Items.OrderBy(i => i.RecordId) }));
		private static IEnumerable<JObject> AttachmentManifest(JObject content) => (content["Attachments"] as JArray ?? new JArray()).OfType<JObject>().Where(a => (string)a["RmsRecordAttachmentId"] != null);
		private static JObject PrepareDisclosure(RecordDocument doc, string profile, List<string> hidden)
		{
			var content = JObject.Parse(doc.ContentJson);
			content["RevisionAttestation"] = JObject.FromObject(new { doc.RevisionNumber, SavedOn = doc.FinalizedOn, RecordedBy = doc.AttestedBy, StatementVersion = doc.AttestationVersion });
			if (profile != RmsRedactionProfiles.FullDisclosure) RecordsDocumentService.Project(content, false, hidden);
			if (profile == RmsRedactionProfiles.NoPersonalIdentifiers)
			{
				foreach (var p in content.Descendants().OfType<JProperty>().Where(p => new[] { "Participants", "AuthorUserId", "PersonnelUserId", "ContactName", "ContactNumber", "InvestigatedByUserId", "Instructors", "Facilitator", "OtherPersonnel", "RecordedBy" }.Contains(p.Name)).ToList())
				{ hidden.Add(p.Path); p.Remove(); }
			}
			return DisclosureContentPolicy.Prepare(content);
		}
		private static string PacketHtml(RmsDisclosureRequest request, JArray produced, JArray documents, List<RmsRedactionEntry> withheld, DateTime now)
		{
			string E(string value) => WebUtility.HtmlEncode(value ?? "");
			var html = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>Records disclosure</title><style>body{font:11px Arial;color:#142235}h1{font-size:23px}h2{font-size:16px}table{width:100%;border-collapse:collapse}td,th{border:1px solid #bbb;padding:4px;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{width:32%}tr{page-break-inside:avoid}.record{page-break-before:always}</style></head><body><h1>Records disclosure ");
			html.Append(E(request.RequestNumber)).Append("</h1><p>").Append(E(request.JurisdictionProfile)).Append(" · Prepared ").Append(now.ToString("u")).Append("</p><h2>Contents</h2><ol>");
			foreach (var item in produced) html.Append("<li>").Append(E((string)item["record_number"])).Append(" · revision ").Append((int)item["revision_number"]).Append("</li>");
			html.Append("</ol><p>Attachments are separate files in the packet. The manifest records each file and checksum.</p>");
			for (var i = 0; i < documents.Count; i++)
			{
				html.Append("<div class=\"record\"><h1>Record ").Append(i + 1).Append(" · ").Append(E((string)produced[i]["record_number"])).Append("</h1><p>Revision checksum ").Append(E((string)produced[i]["revision_checksum"])).Append("</p>");
				RecordsDocumentService.RenderSections(html, (JObject)documents[i]["content"]); html.Append("</div>");
			}
			html.Append("<h2>Withholding log</h2><table><tr><th>Record / field</th><th>Authority and reason</th></tr>");
			foreach (var entry in withheld) html.Append("<tr><td>").Append(E(entry.RecordId + " / " + entry.Field)).Append("</td><td>").Append(E(entry.Authority + ": " + entry.Basis)).Append("</td></tr>");
			return html.Append("</table></body></html>").ToString();
		}
	}
}
