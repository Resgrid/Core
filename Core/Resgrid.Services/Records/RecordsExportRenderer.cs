using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Column values and file bodies for department exports. Values are strings by design: an agency import
	/// sees exactly what the department reviewed, timestamps are ISO-8601 UTC, and every CSV cell is quoted and
	/// guarded against spreadsheet formula injection (a leading =, +, -, @, tab or CR is prefixed with an apostrophe).
	/// </summary>
	public static class RecordsExportRenderer
	{
		public static async Task<string> ValueAsync(string key, RecordsExportContext context, Department department, NameResolver names)
		{
			var record = context.Operational?.Record;
			var details = context.Operational?.Details;
			var report = context.Incident?.Report;
			var incident = context.Incident;

			switch (key)
			{
				case "record.id": return context.RecordId;
				case "record.kind": return record != null ? "Operational" : "IncidentReport";
				case "record.number": return record?.RecordNumber ?? report?.RecordNumber ?? record?.DraftReference ?? report?.DraftReference;
				case "record.definition_key": return record?.DefinitionKey ?? report?.DefinitionKey;
				case "record.type": return record?.RecordType.HasValue == true ? ((RmsOperationalRecordType)record.RecordType.Value).ToString() : report != null ? "NerisIncident" : null;
				case "record.state": return record != null ? ((RmsRecordState)record.State).ToString() : report != null ? ((RmsRecordState)report.State).ToString() : null;
				case "record.revision_number": return Num(record?.RevisionCount ?? report?.RevisionCount);
				case "record.revision_checksum": return context.Operational?.Revisions?.FirstOrDefault(r => r.RmsRevisionId == record?.CurrentRevisionId)?.Checksum ?? incident?.Revisions?.FirstOrDefault(r => r.RmsRevisionId == report?.CurrentRevisionId)?.Checksum;
				case "record.station_group_id": return Num(record?.StationGroupId ?? report?.StationGroupId);
				case "record.station_group_name": return await names.GroupAsync(record?.StationGroupId ?? report?.StationGroupId);
				case "record.author_user_id": return record?.AuthorUserId ?? report?.AuthorUserId;
				case "record.author_name": return await names.PersonAsync(record?.AuthorUserId ?? report?.AuthorUserId);
				case "record.started_on": return Iso(record?.StartedOn ?? report?.CallCreatedOn);
				case "record.ended_on": return Iso(record?.EndedOn ?? report?.IncidentClearedOn);
				case "record.duration_minutes":
				{
					var start = record?.StartedOn ?? report?.CallCreatedOn;
					var end = record?.EndedOn ?? report?.IncidentClearedOn;
					return start.HasValue && end.HasValue && end >= start ? Math.Round((end.Value - start.Value).TotalMinutes, 1).ToString(CultureInfo.InvariantCulture) : null;
				}
				case "record.created_on": return Iso(record?.CreatedOn ?? report?.CreatedOn);
				case "record.finalized_on": return Iso(record?.FinalizedOn ?? report?.FinalizedOn);
				case "record.external_id": return record?.ExternalId ?? report?.NerisIncidentId;

				case "call.id": return Num(record?.CallId ?? report?.CallId);
				case "call.number": return details?.CallNumber ?? report?.IncidentNumber;
				case "call.type": return details?.CallType ?? report?.DispatchIncidentCode;
				case "call.priority": return Num(details?.CallPriority);
				case "call.logged_on": return Iso(details?.CallLoggedOn ?? report?.CallCreatedOn);
				case "call.name": return details?.CallName ?? report?.DisplaySummary;
				case "call.address": return details?.CallAddress ?? incident?.Location?.AddressText;
				case "call.nature": return details?.CallNature;

				case "participants.count": return Num(context.Operational?.Participants?.Count ?? 0);
				case "participants.user_ids": return Join(context.Operational?.Participants?.Select(p => p.UserId));
				case "participants.names":
				{
					if (context.Operational?.Participants == null) return string.Empty;
					var list = new List<string>();
					foreach (var p in context.Operational.Participants) list.Add(string.IsNullOrWhiteSpace(p.DisplayNameSnapshot) ? await names.PersonAsync(p.UserId) : p.DisplayNameSnapshot);
					return Join(list);
				}
				case "units.count": return Num(context.Operational?.Units?.Count ?? incident?.Units?.Count ?? 0);
				case "units.names": return Join(context.Operational?.Units?.Select(u => u.UnitNameSnapshot ?? u.UnitId.ToString(CultureInfo.InvariantCulture)) ?? incident?.Units?.Select(u => u.UnitNameSnapshot ?? Convert.ToString(u.UnitId, CultureInfo.InvariantCulture)));
				case "units.first_dispatched": return Iso(context.Operational?.Units?.Where(u => u.Dispatched.HasValue).Min(u => u.Dispatched) ?? incident?.Units?.Where(u => u.DispatchedOn.HasValue).Min(u => u.DispatchedOn));
				case "units.first_on_scene": return Iso(context.Operational?.Units?.Where(u => u.OnScene.HasValue).Min(u => u.OnScene) ?? incident?.Units?.Where(u => u.OnSceneOn.HasValue).Min(u => u.OnSceneOn));
				case "units.last_cleared": return Iso(context.Operational?.Units?.Where(u => u.Released.HasValue).Max(u => u.Released) ?? incident?.Units?.Where(u => u.ClearedOn.HasValue).Max(u => u.ClearedOn));
				case "attachments.count": return Num(context.Operational?.Attachments?.Count ?? incident?.Attachments?.Count ?? 0);

				case "details.type": return details?.Type;
				case "details.course": return details?.Course;
				case "details.course_code": return details?.CourseCode;
				case "details.instructors": return details?.Instructors;
				case "details.facilitator": return details?.Facilitator;
				case "details.other_agencies": return details?.OtherAgencies;
				case "details.other_units": return details?.OtherUnits;
				case "details.unit_id": return Num(details?.UnitId);
				case "details.activity_on": return Iso(details?.ActivityOn);
				case "details.narrative": return details?.Narrative;
				case "details.initial_report": return details?.InitialReport;
				case "details.cause": return details?.Cause;
				case "details.location": return details?.Location;
				case "details.contact_name": return details?.ContactName;
				case "details.contact_number": return details?.ContactNumber;
				case "details.investigated_by_user_id": return details?.InvestigatedByUserId;
				case "details.other_personnel": return details?.OtherPersonnel;
				case "details.body_location": return details?.BodyLocation;
				case "details.pronounced_deceased_by": return details?.PronouncedDeceasedBy;
				case "details.case_number": return details?.CaseNumber;
				case "details.destination": return details?.Destination;

				case "incident.number": return report?.IncidentNumber;
				case "incident.reporting_entity_id": return report?.ReportingEntityId;
				case "incident.neris_incident_id": return report?.NerisIncidentId;
				case "incident.dispatch_code": return report?.DispatchIncidentCode;
				case "incident.primary_type": return incident?.Types?.FirstOrDefault(t => t.IsPrimary)?.TypeCode ?? incident?.Types?.FirstOrDefault()?.TypeCode;
				case "incident.type_codes": return Join(incident?.Types?.OrderBy(t => t.Ordinal).Select(t => t.TypeCode));
				case "incident.call_created_on": return Iso(report?.CallCreatedOn);
				case "incident.cleared_on": return Iso(report?.IncidentClearedOn);
				case "incident.disposition": return report?.Disposition;
				case "incident.last_submission_state": return report?.LastSubmissionState.HasValue == true ? ((RmsSubmissionState)report.LastSubmissionState.Value).ToString() : null;
				case "incident.aid_count": return Num(incident?.Aids?.Count ?? 0);
				case "incident.tactic_codes": return Join(incident?.Tactics?.OrderBy(t => t.Ordinal).Select(t => t.TacticCode));
				case "incident.location_use": return incident?.Location?.LocationUse;
				case "incident.address": return incident?.Location?.AddressText;
				case "incident.narrative": return incident?.Narrative?.Narrative;
				case "incident.casualty_count": return Num(incident?.Casualties?.Count ?? 0);
				case "incident.exposure_count": return Num(incident?.Exposures?.Count ?? 0);
				default: return null;
			}
		}

		public static byte[] RenderCsv(RmsExportTemplate template, IReadOnlyList<RecordsExportField> columns, IReadOnlyList<Dictionary<string, string>> rows)
		{
			var delimiter = string.IsNullOrEmpty(template.Delimiter) ? "," : template.Delimiter;
			var builder = new StringBuilder();
			if (template.IncludeHeader)
				builder.AppendLine(string.Join(delimiter, columns.Select(c => Cell(c.Key, delimiter))));
			foreach (var row in rows)
				builder.AppendLine(string.Join(delimiter, columns.Select(c => Cell(row.TryGetValue(c.Key, out var v) ? v : string.Empty, delimiter))));
			// UTF-8 with BOM: the one encoding every spreadsheet an agency clerk opens reads correctly.
			return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
		}

		public static byte[] RenderJson(RmsExportTemplate template, IReadOnlyList<RecordsExportField> columns, IReadOnlyList<Dictionary<string, string>> rows, RecordsExportRequest request, DateTime now)
		{
			var payload = new
			{
				format = "resgrid.records.export.v1",
				template = template.TemplateKey,
				name = template.Name,
				generated_on = now,
				window_start = request.WindowStart,
				window_end = request.WindowEnd,
				record_id = request.RecordId,
				columns = columns.Select(c => new { key = c.Key, label = c.Label }),
				rows = rows.Select(r => columns.ToDictionary(c => c.Key, c => r.TryGetValue(c.Key, out var v) ? v : string.Empty))
			};
			return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload, Formatting.Indented));
		}

		public static byte[] RenderPdf(IPdfProvider pdf, RmsExportTemplate template, IReadOnlyList<RecordsExportField> columns, IReadOnlyList<Dictionary<string, string>> rows, RecordsExportRequest request, Department department, DateTime now)
		{
			var html = new StringBuilder();
			html.Append("<html><head><meta charset=\"utf-8\" /><style>body{font-family:Arial,Helvetica,sans-serif;font-size:9pt;margin:18px;} h1{font-size:14pt;margin:0 0 4px 0;} .meta{color:#555;margin-bottom:10px;} table{border-collapse:collapse;width:100%;} th,td{border:1px solid #999;padding:3px 5px;vertical-align:top;word-break:break-word;} th{background:#eee;text-align:left;} .foot{margin-top:12px;color:#555;font-size:8pt;}</style></head><body>");
			html.Append("<h1>").Append(WebUtility.HtmlEncode(template.Name ?? template.TemplateKey)).Append("</h1>");
			html.Append("<div class=\"meta\">").Append(WebUtility.HtmlEncode(department?.Name ?? string.Empty));
			if (request.WindowStart.HasValue || request.WindowEnd.HasValue)
				html.Append(" · ").Append(WebUtility.HtmlEncode(Iso(request.WindowStart))).Append(" to ").Append(WebUtility.HtmlEncode(Iso(request.WindowEnd)));
			html.Append(" · generated ").Append(WebUtility.HtmlEncode(Iso(now))).Append(" · ").Append(rows.Count).Append(" record(s)</div>");
			html.Append("<table><thead><tr>");
			foreach (var column in columns)
				html.Append("<th>").Append(WebUtility.HtmlEncode(column.Label)).Append("</th>");
			html.Append("</tr></thead><tbody>");
			foreach (var row in rows)
			{
				html.Append("<tr>");
				foreach (var column in columns)
					html.Append("<td>").Append(WebUtility.HtmlEncode(row.TryGetValue(column.Key, out var v) ? v : string.Empty)).Append("</td>");
				html.Append("</tr>");
			}
			html.Append("</tbody></table>");
			html.Append("<div class=\"foot\">Resgrid Records export · template ").Append(WebUtility.HtmlEncode(template.TemplateKey)).Append("</div>");
			html.Append("</body></html>");
			var bytes = pdf.ConvertHtmlToPdf(html.ToString(), "Letter");
			if (bytes == null || bytes.Length < 4)
				throw new InvalidOperationException("The PDF provider did not produce a document.");
			return bytes;
		}

		/// <summary>RFC 4180 quoting plus the spreadsheet formula guard.</summary>
		public static string Cell(string value, string delimiter)
		{
			value ??= string.Empty;
			if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@' || value[0] == '\t' || value[0] == '\r'))
				value = "'" + value;
			var mustQuote = value.Contains(delimiter) || value.Contains('"') || value.Contains('\n') || value.Contains('\r') || value.StartsWith(" ") || value.EndsWith(" ");
			return mustQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
		}

		private static string Iso(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : null;
		private static string Num(long? value) => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
		private static string Join(IEnumerable<string> values) => values == null ? string.Empty : string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
	}
}
