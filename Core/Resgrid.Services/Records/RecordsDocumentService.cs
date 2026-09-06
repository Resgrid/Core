using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>Departmental copies use immutable content and current access. National payloads are a separate contract.</summary>
	public sealed class RecordsDocumentService : IRecordsDocumentService
	{
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsIncidentReportsRepository _reports;
		private readonly IRmsIncidentAnalysesRepository _analyses;
		private readonly IRmsRevisionsRepository _revisions;
		private readonly IIncidentReportsService _incidents;
		private readonly IDepartmentProfileMediaService _branding;
		private readonly IRecordsPrintLayoutService _layouts;
		private readonly IPdfProvider _pdf;
		private readonly IRecordsEvidenceService _evidence;
		private readonly IRecordsUdfService _udf;
		public RecordsDocumentService(IRecordsAuthorizationService authorization, IRmsOperationalRecordsRepository records, IRmsIncidentReportsRepository reports,
			IRmsIncidentAnalysesRepository analyses, IRmsRevisionsRepository revisions, IIncidentReportsService incidents,
			IDepartmentProfileMediaService branding, IRecordsPrintLayoutService layouts, IPdfProvider pdf, IRecordsEvidenceService evidence, IRecordsUdfService udf)
		{ _authorization = authorization; _records = records; _reports = reports; _analyses = analyses; _revisions = revisions; _incidents = incidents; _branding = branding; _layouts = layouts; _pdf = pdf; _evidence = evidence; _udf = udf; }

		public async Task<RecordDocument> GetAsync(int departmentId, string userId, string recordId, RmsRecordKind kind, string revisionId = null, bool exporting = false)
		{
			string parentId = recordId, currentRevisionId = null, number = null;
			if (kind == RmsRecordKind.IncidentAnalysis)
			{
				var analysis = await _analyses.GetByIdForDepartmentAsync(departmentId, recordId);
				if (analysis == null || analysis.DeletedOn.HasValue) return null;
				parentId = analysis.IncidentReportId; currentRevisionId = analysis.CurrentRevisionId;
			}
			if (!await _authorization.CanUserViewRecordAsync(userId, parentId, departmentId)
				|| exporting && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ExportRecords)) throw new UnauthorizedAccessException();
			if (kind == RmsRecordKind.Operational)
			{
				var record = await _records.GetByIdForDepartmentAsync(departmentId, recordId);
				if (record == null || record.DeletedOn.HasValue || record.PurgedOn.HasValue) return null;
				currentRevisionId = record.CurrentRevisionId; number = record.RecordNumber ?? record.DraftReference;
			}
			else if (kind == RmsRecordKind.IncidentReport || kind == RmsRecordKind.IncidentAnalysis)
			{
				var report = await _reports.GetByIdForDepartmentAsync(departmentId, parentId);
				if (report == null || report.DeletedOn.HasValue || report.PurgedOn.HasValue) return null;
				if (kind == RmsRecordKind.IncidentReport) currentRevisionId = report.CurrentRevisionId;
				number = report.RecordNumber ?? report.DraftReference;
			}
			else throw new ArgumentException("Unsupported record kind.");
			revisionId ??= currentRevisionId;
			if (string.IsNullOrWhiteSpace(revisionId)) return null;
			var revision = await _revisions.GetByIdForDepartmentAsync(departmentId, revisionId);
			if (revision == null || revision.RecordId != recordId || revision.RecordKind != (int)kind) return null;
			if (RecordSnapshotSerializer.Checksum(revision.SnapshotJson) != revision.Checksum) throw new InvalidOperationException("The revision checksum does not match.");
			JObject content;
			if (kind == RmsRecordKind.IncidentReport)
			{
				var snapshot = await _incidents.BuildSnapshotAsync(departmentId, recordId, revisionId);
				if (snapshot == null) return null;
				content = JObject.FromObject(snapshot);
			}
			else content = JObject.Parse(revision.SnapshotJson);
			if (kind == RmsRecordKind.Operational && ((int?)content["SnapshotVersion"] ?? 1) < 2)
				content["Evidence"] = JArray.FromObject(await _evidence.GetForRecordAsync(departmentId, recordId, revisionId, true) ?? new List<RmsEvidenceArtifact>());
			var document = new RecordDocument { RecordId = recordId, RecordKind = kind, RecordNumber = number, RevisionId = revisionId, RevisionNumber = revision.RevisionNumber,
				OriginalChecksum = revision.Checksum, FinalizedOn = revision.CreatedOn, AttestedBy = revision.ActorUserId, AttestationVersion = revision.AttestationStatementVersion };
			await ProjectCustomFieldsAsync(departmentId, userId, document.WithheldFields, content);
			Project(content, await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords), document.WithheldFields);
			document.ContentJson = content.ToString(Formatting.None); document.ContentChecksum = RecordSnapshotSerializer.Checksum(document.ContentJson);
			if (!await _authorization.CanUserViewRecordAsync(userId, parentId, departmentId)
				|| exporting && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ExportRecords)) throw new UnauthorizedAccessException();
			return document;
		}

		/// <summary>Clone before calling. Opaque restricted JSON is withheld as a whole, including all nested aliases.</summary>
		public static void Project(JObject content, bool restricted, List<string> withheld)
		{
			void Hide(JObject obj, string field) { var p = obj?.Property(field); if (p == null) return; if (p.Value.Type != JTokenType.Null) withheld.Add(p.Path); p.Remove(); }
			if (!restricted)
			{
				foreach (var field in ((content["CustomFields"] as JObject)?["Fields"] as JArray ?? new JArray()).OfType<JObject>().ToList())
					if ((int?)(field["Field"] as JObject)?["RmsClassification"] != 0) { withheld.Add(field.Path); field.Remove(); }
				foreach (var field in RecordSnapshotSerializer.RestrictedDetailFields) Hide(content["Details"] as JObject, field);
				foreach (var casualty in (content["Casualties"] as JArray ?? new JArray()).OfType<JObject>())
					foreach (var field in new[] { "PersonnelUserId", "Rank", "BirthMonthYear", "Gender", "Race", "InjuryDetailJson", "DetailJson" }) Hide(casualty, field);
				foreach (var vehicle in (content["Vehicles"] as JArray ?? new JArray()).OfType<JObject>())
					foreach (var field in new[] { "Vin", "LicensePlate", "LicenseState", "DetailJson" }) Hide(vehicle, field);
				foreach (var evidence in (content["Evidence"] as JArray ?? new JArray()).OfType<JObject>().ToList())
					if ((int?)evidence["Classification"] != (int)RmsEvidenceClassification.Unrestricted) { withheld.Add(evidence.Path); evidence.Replace(new JObject { ["Withheld"] = true }); }
				foreach (var attachment in (content["Attachments"] as JArray ?? new JArray()).OfType<JObject>().ToList())
					if ((bool?)attachment["IsProtected"] == true || (int?)attachment["Classification"] != (int)RmsEvidenceClassification.Unrestricted) { withheld.Add(attachment.Path); attachment.Replace(new JObject { ["Withheld"] = true }); }
			}
			// These are storage/runtime fields, not authored departmental content.
			foreach (var property in content.Descendants().OfType<JProperty>().Where(p => new[] { "Data", "StorageReference", "ProtectionId", "ProtectedEnvelope", "IdValue", "TableName", "IdName", "IdType", "IgnoredProperties" }.Contains(p.Name)).ToList()) property.Remove();
		}

		public async Task<string> RenderHtmlAsync(int departmentId, string userId, RecordDocument document)
		{
			// Re-read before rendering so a previously built object is not an authorization token.
			var current = await GetAsync(departmentId, userId, document.RecordId, document.RecordKind, document.RevisionId);
			if (current == null || current.ContentChecksum != document.ContentChecksum) throw new UnauthorizedAccessException("Record access or content changed; reload the revision.");
			document = current;
			var branding = await _branding.GetBrandingAsync(departmentId);
			var layout = await _layouts.GetDepartmentDefaultAsync(departmentId); var config = layout?.Config ?? RecordsPrintLayoutConfig.Default();
			var html = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>Department record</title><style>@page{size:" + RecordsPrintLayoutConfig.NormalizePageSize(config.PageSize) + ";margin:18mm}body{font:12px Arial,sans-serif;color:#142235}h1{font-size:23px}h2{margin-top:16px;font-size:16px}table{width:100%;border-collapse:collapse}th,td{padding:4px;border:1px solid #ccd2da;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{width:32%}tr{page-break-inside:avoid}pre{white-space:pre-wrap}footer{font-size:10px;margin-top:16px;page-break-inside:avoid}.withheld{border:1px solid #b65;padding:10px}.watermark{color:#777;font-weight:bold}</style></head><body>");
			if (config.ShowLogo && branding?.HasLogo == true)
			{
				var logo = await _branding.GetMediaAsync(departmentId, DepartmentProfileMediaKind.PrintHeader);
				if (logo?.Data?.Length > 0 && new[] { "image/png", "image/jpeg" }.Contains(logo.ContentType)) html.Append("<img alt=\"Department logo\" style=\"max-width:180px;max-height:85px\" src=\"data:").Append(logo.ContentType).Append(";base64,").Append(Convert.ToBase64String(logo.Data)).Append("\">");
			}
			html.Append("<h1>").Append(E(config.UseShortName ? branding?.ShortName : branding?.DisplayName)).Append("</h1>");
			foreach (var line in new[] { config.ShowAddress ? branding?.AddressText : null, config.ShowPhone ? branding?.PhoneNumber : null, config.ShowWebsite ? branding?.Website : null, config.LetterheadLine1, config.LetterheadLine2 })
				if (!string.IsNullOrWhiteSpace(line)) html.Append("<div>").Append(E(line)).Append("</div>");
			html.Append("<h2>Complete department record ").Append(E(document.RecordNumber)).Append(" — revision ").Append(document.RevisionNumber).Append("</h2><p>Saved ").Append(E(document.FinalizedOn.ToString("u"))).Append(" · Attested by ").Append(E(document.AttestedBy)).Append(" · Statement ").Append(E(document.AttestationVersion)).Append("</p>");
			if (document.WithheldFields.Count > 0) html.Append("<p class=\"withheld\">Some fields are withheld under your current access permissions.</p>");
			if (!string.IsNullOrWhiteSpace(config.WatermarkLabel)) html.Append("<p class=\"watermark\">").Append(E(config.WatermarkLabel)).Append("</p>");
			RenderSections(html, JObject.Parse(document.ContentJson));
			html.Append("<footer>").Append(E(config.FooterText)).Append("<p>Revision ").Append(E(document.RevisionId)).Append(" · Original checksum ").Append(E(document.OriginalChecksum)).Append("</p><p>Copy checksum ").Append(E(document.ContentChecksum)).Append(" · Layout ").Append(E(layout?.LayoutVersion)).Append(" · Printed by ").Append(E(userId)).Append(" at ").Append(DateTime.UtcNow.ToString("u")).Append("</p></footer></body></html>");
			await RequireCurrentDocumentAsync(departmentId, userId, document);
			return html.ToString();
		}
		public async Task<byte[]> RenderPdfAsync(int departmentId, string userId, RecordDocument document)
		{
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ExportRecords)) throw new UnauthorizedAccessException();
			var pageSize = (await _layouts.GetDepartmentDefaultAsync(departmentId))?.Config?.PageSize;
			var bytes = _pdf.ConvertHtmlToPdf(await RenderHtmlAsync(departmentId, userId, document), RecordsPrintLayoutConfig.NormalizePageSize(pageSize));
			await RequireCurrentDocumentAsync(departmentId, userId, document, true);
			return bytes;
		}

		private async Task RequireCurrentDocumentAsync(int departmentId, string userId, RecordDocument document, bool exporting = false)
		{
			var current = await GetAsync(departmentId, userId, document.RecordId, document.RecordKind, document.RevisionId, exporting);
			if (current == null || current.ContentChecksum != document.ContentChecksum) throw new UnauthorizedAccessException("Record access or content changed; reload the revision.");
		}

		public async Task<byte[]> RenderDiffPdfAsync(int departmentId, string userId, string recordId, RmsRecordKind kind, string fromRevisionId, string toRevisionId)
		{
			if (string.IsNullOrWhiteSpace(fromRevisionId) || string.IsNullOrWhiteSpace(toRevisionId)) throw new ArgumentException("Choose both revisions to compare.");
			var branding = await _branding.GetBrandingAsync(departmentId);
			var layout = await _layouts.GetDepartmentDefaultAsync(departmentId);
			var config = layout?.Config ?? RecordsPrintLayoutConfig.Default();
			var from = await GetAsync(departmentId, userId, recordId, kind, fromRevisionId, true);
			var to = await GetAsync(departmentId, userId, recordId, kind, toRevisionId, true);
			if (from == null || to == null) throw new InvalidOperationException("A requested revision is unavailable.");
			var changes = await DiffAsync(departmentId, userId, from, to);
			var html = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>Report revision changes</title><style>body{font:12px Arial,sans-serif;color:#142235}table{width:100%;border-collapse:collapse;table-layout:fixed}th,td{border:1px solid #ccd2da;padding:5px;text-align:left;vertical-align:top;white-space:pre-wrap;overflow-wrap:anywhere}thead{display:table-header-group}tr{page-break-inside:avoid}footer{font-size:10px;overflow-wrap:anywhere;margin-top:16px}h1{font-size:22px}</style></head><body>");
			html.Append("<h1>").Append(E(config.UseShortName ? branding?.ShortName : branding?.DisplayName)).Append("</h1><h2>Report ").Append(E(to.RecordNumber)).Append(" — revision ").Append(from.RevisionNumber).Append(" to ").Append(to.RevisionNumber).Append("</h2>");
			html.Append("<p>Only changes visible under your current permissions are shown. Department custom fields are included.</p>");
			if (from.WithheldFields.Count > 0 || to.WithheldFields.Count > 0) html.Append("<p>Some fields are withheld under your current permissions.</p>");
			if (!string.IsNullOrWhiteSpace(config.WatermarkLabel)) html.Append("<p>").Append(E(config.WatermarkLabel)).Append("</p>");
			if (changes.Count == 0) html.Append("<p>No visible content changed.</p>");
			else
			{
				html.Append("<table><thead><tr><th>Field</th><th>Before</th><th>After</th></tr></thead><tbody>");
				foreach (var change in changes) html.Append("<tr><th>").Append(E(change.FieldLabel ?? change.FieldKey)).Append("</th><td>").Append(E(change.OldValue)).Append("</td><td>").Append(E(change.NewValue)).Append("</td></tr>");
				html.Append("</tbody></table>");
			}
			html.Append("<footer>").Append(E(config.FooterText)).Append("<p>From ").Append(E(from.RevisionId)).Append(" · Original checksum ").Append(E(from.OriginalChecksum)).Append("</p><p>To ").Append(E(to.RevisionId)).Append(" · Original checksum ").Append(E(to.OriginalChecksum)).Append("</p><p>Printed by ").Append(E(userId)).Append(" at ").Append(DateTime.UtcNow.ToString("u")).Append("</p></footer></body></html>");
			var bytes = _pdf.ConvertHtmlToPdf(html.ToString(), RecordsPrintLayoutConfig.NormalizePageSize(config.PageSize));
			await RequireCurrentDocumentAsync(departmentId, userId, from, true);
			await RequireCurrentDocumentAsync(departmentId, userId, to, true);
			return bytes;
		}
		private static string E(string text) => WebUtility.HtmlEncode(text ?? "");
		private static string Label(string text) => Regex.Replace(text.Replace('_', ' '), "([a-z])([A-Z])", "$1 $2");
		private static readonly HashSet<string> PrintMetadata = new HashSet<string>(StringComparer.Ordinal)
		{
			"DepartmentId", "RecordId", "RevisionId", "SnapshotVersion", "DefinitionVersion", "DefinitionKey", "UdfDefinitionId", "CreatedOn", "ModifiedOn", "RowVersion", "DeletedOn", "IsProtected", "ProtectedCatalogVersion", "Ordinal", "IsCurrent", "SupersededByArtifactId", "SupersededOn",
			"OwnerUserId", "ReviewerUserId", "ApproverUserId", "ReviewDueOn", "SubmittedForReviewOn", "ReturnedOn", "ReturnCount", "ApprovedOn", "CurrentRevisionId", "RevisionCount", "AmendsRevisionId", "LastSubmissionId", "LastSubmissionState", "LastSubmittedOn", "AcceptedOn", "RejectedOn", "RejectionSummary", "LifecyclePreset", "OriginClient", "ModifiedByUserId", "PurgedOn", "PurgedByUserId"
		};
		private static bool IsPrintMetadata(JProperty property) => PrintMetadata.Contains(property.Name)
			|| property.Name.StartsWith("Rms", StringComparison.Ordinal) && property.Name.EndsWith("Id", StringComparison.Ordinal)
			// State on an address is customer content. Only the aggregate header's lifecycle state is metadata.
			|| property.Name == "State" && (property.Parent?.Path is "" or "Report" or "Analysis");
		internal static void RenderSections(StringBuilder html, JObject content)
		{
			var order = new[] { "Report", "Analysis", "Location", "Types", "Units", "Aids", "Tactics", "Narrative", "Details", "Participants", "Modules", "Resources", "Casualties", "Exposures", "Properties", "Vehicles", "Facts", "DispatchComments", "SpecialModifiers", "Evidence", "Attachments" };
			foreach (var section in content.Properties().OrderBy(p => { var i = Array.IndexOf(order, p.Name); return i < 0 ? order.Length : i; }))
			{
				if (IsPrintMetadata(section) || section.Value.Type == JTokenType.Null) continue;
				if (section.Name == "CustomFields")
				{
					var custom = section.Value.ToObject<RecordUdfSection>();
					if (custom?.Fields.Count > 0)
					{
						html.Append("<h2>Department custom fields</h2><p>Captured form version ").Append(custom.ExtensionVersion).Append(". These fields are excluded from NERIS submission.</p><table><tbody>");
						foreach (var field in custom.Fields) html.Append("<tr><th>").Append(E(field.Field.Label)).Append("</th><td style=\"white-space:pre-wrap\">").Append(E(field.Value ?? "")).Append("</td></tr>");
						html.Append("</tbody></table>");
					}
					continue;
				}
				var rows = new List<(string Label, string Value)>(); PrintableRows(section.Value, "", rows, 0);
				if (rows.Count == 0) continue;
				html.Append("<h2>").Append(E(Label(section.Name))).Append("</h2><table><tbody>");
				foreach (var row in rows) html.Append("<tr><th>").Append(E(row.Label)).Append("</th><td style=\"white-space:pre-wrap\">").Append(section.Name == "Details" && row.Label == "Narrative" ? Resgrid.Framework.RecordNarrativeFormatter.Render(row.Value) : E(row.Value)).Append("</td></tr>");
				html.Append("</tbody></table>");
			}
		}
		private static void PrintableRows(JToken value, string path, List<(string Label, string Value)> rows, int depth)
		{
			if (depth > 32) throw new InvalidOperationException("Record content exceeds the supported nesting depth.");
			if (value is JObject obj)
			{
				foreach (var property in obj.Properties())
					if (!IsPrintMetadata(property))
						PrintableRows(property.Value, string.IsNullOrEmpty(path) ? Label(property.Name) : path + " / " + Label(property.Name), rows, depth + 1);
			}
			else if (value is JArray array) for (var i = 0; i < array.Count; i++) PrintableRows(array[i], path + "Item " + (i + 1), rows, depth + 1);
			else if (value.Type != JTokenType.Null)
			{
				var text = value.Type == JTokenType.Date ? value.ToObject<DateTime>().ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
					: value.Type == JTokenType.Boolean ? value.Value<bool>() ? "Yes" : "No" : value.ToString();
				if (value.Type == JTokenType.Integer && value.Parent is JProperty property)
				{
					Type enumType = property.Name switch { "SourceKind" or "TimesSourceKind" => typeof(RmsSourceKind), "RecordKind" => typeof(RmsRecordKind), "Classification" => typeof(RmsEvidenceClassification), _ => null };
					if (enumType != null && Enum.IsDefined(enumType, value.Value<int>())) text = Label(Enum.GetName(enumType, value.Value<int>()));
				}
				if (string.IsNullOrWhiteSpace(text) || value.Type == JTokenType.Date && value.ToObject<DateTime>().Year == 1) return;
				if (value.Type == JTokenType.String && (text.TrimStart().StartsWith("{") || text.TrimStart().StartsWith("[")))
				{ try { var parsed = JToken.Parse(text); PrintableRows(parsed, path.Replace(" Json", ""), rows, depth + 1); return; } catch (JsonException) { } }
				rows.Add((string.IsNullOrEmpty(path) ? "Value" : path, text));
			}
		}
		public async Task<List<RecordFieldDiff>> DiffAsync(int departmentId, string userId, RecordDocument from, RecordDocument to)
		{
			if (from.RecordId != to.RecordId || from.RecordKind != to.RecordKind) throw new ArgumentException("Compare revisions of the same record.");
			// Reload both, then project them together under the last live field scope. A revoked
			// value must never appear as a removal merely because the two reads saw different grants.
			var currentFrom = await GetAsync(departmentId, userId, from.RecordId, from.RecordKind, from.RevisionId);
			var currentTo = await GetAsync(departmentId, userId, to.RecordId, to.RecordKind, to.RevisionId);
			if (currentFrom == null || currentTo == null) throw new UnauthorizedAccessException();
			var leftContent = JObject.Parse(currentFrom.ContentJson); var rightContent = JObject.Parse(currentTo.ContentJson);
			await ProjectCustomFieldsAsync(departmentId, userId, new List<string>(), leftContent, rightContent);
			var restricted = await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords);
			Project(leftContent, restricted, new List<string>()); Project(rightContent, restricted, new List<string>());
			var parentId = from.RecordId;
			if (from.RecordKind == RmsRecordKind.IncidentAnalysis)
			{
				var analysis = await _analyses.GetByIdForDepartmentAsync(departmentId, from.RecordId);
				if (analysis == null || analysis.DeletedOn.HasValue) throw new UnauthorizedAccessException();
				parentId = analysis.IncidentReportId;
			}
			if (!await _authorization.CanUserViewRecordAsync(userId, parentId, departmentId)) throw new UnauthorizedAccessException();
			var left = Leaves(leftContent); var right = Leaves(rightContent);
			return left.Keys.Union(right.Keys).OrderBy(k => k, StringComparer.Ordinal).Where(k => left.GetValueOrDefault(k) != right.GetValueOrDefault(k))
				.Select(k => new RecordFieldDiff { FieldKey = k, FieldLabel = DiffLabel(k, leftContent, rightContent), Section = k.Split('.')[0], OldValue = left.GetValueOrDefault(k), NewValue = right.GetValueOrDefault(k) }).ToList();
		}
		private static string DiffLabel(string path, JObject left, JObject right)
		{
			var match = Regex.Match(path, @"^CustomFields\.Fields\[\d+\]\.Value$");
			if (!match.Success) return null;
			var labelPath = path.Substring(0, path.Length - "Value".Length) + "Field.Label";
			var label = (string)(right.SelectToken(labelPath) ?? left.SelectToken(labelPath));
			return string.IsNullOrWhiteSpace(label) ? null : "Department custom field: " + label;
		}
		private async Task ProjectCustomFieldsAsync(int department, string user, List<string> withheld, params JObject[] contents)
		{
			var originals=contents.Select(c=>c["CustomFields"]).OfType<JObject>().ToList();
			if(originals.Count==0) return;
			var combined=new RecordUdfSection {Fields=originals.SelectMany(o=>o.ToObject<RecordUdfSection>().Fields).ToList()};
			var projected=await _udf.ProjectAsync(department,user,combined);
			var allowed=(projected?.Fields ?? new List<RecordUdfField>()).Select(f=>f.Field.UdfFieldId).ToHashSet(StringComparer.Ordinal);
			if(projected==null || projected.Fields.Count<combined.Fields.Count) withheld.Add("CustomFields");
			foreach(var original in originals)
				foreach(var field in (original["Fields"] as JArray ?? new JArray()).OfType<JObject>().ToList())
					if(!allowed.Contains((string)(field["Field"] as JObject)?["UdfFieldId"])) field.Remove();
		}
		private static Dictionary<string, string> Leaves(JObject obj) => obj.Descendants().OfType<JValue>()
			.Where(v => !v.Ancestors().OfType<JProperty>().Any(IsPrintMetadata))
			.ToDictionary(v => v.Path, v => v.Type == JTokenType.Null ? null : v.ToString(CultureInfo.InvariantCulture));
	}
}

