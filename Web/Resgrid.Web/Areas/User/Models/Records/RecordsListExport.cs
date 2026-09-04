using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Web.Areas.User.Models.Records
{
	/// <summary>One exported queue row: the viewer-authorized, safe projection fields only (RMS plan section 4.10).</summary>
	public class RecordsListExportRow
	{
		public string RecordNumber { get; set; }
		public string DraftReference { get; set; }
		public string Type { get; set; }
		public string DefinitionKey { get; set; }
		public string State { get; set; }
		public string Summary { get; set; }
		public string OccurredOn { get; set; }
		public string Author { get; set; }
		public string Station { get; set; }
		public string CallNumber { get; set; }
		public string CreatedOn { get; set; }
		public string FinalizedOn { get; set; }
		public string RecordId { get; set; }
	}

	/// <summary>
	/// List/search tabular export (RMS plan section 4.10): CSV, and the same rows as JSON, from an authorized
	/// queue query. Columns come from <see cref="RmsRecordSearchProjection"/>, which already excludes narrative,
	/// restricted sections and every protected-candidate field, so nothing here needs a second redaction pass.
	/// Data exports carry no branding.
	/// </summary>
	public static class RecordsListExport
	{
		/// <summary>Upper bound on rows per export; a wider result set must be narrowed by filter.</summary>
		public const int MaxRows = 10000;

		public const string JsonFormat = "resgrid.records-list.v1";

		public static readonly string[] Columns =
		{
			"RecordNumber", "DraftReference", "Type", "DefinitionKey", "State", "Summary", "OccurredOn",
			"Author", "Station", "CallNumber", "CreatedOn", "FinalizedOn", "RecordId"
		};

		public static List<RecordsListExportRow> BuildRows(IEnumerable<RmsRecordSearchProjection> projections,
			IDictionary<string, string> personnelNames, IDictionary<int, string> groupNames, Department department)
		{
			string Name(string userId) => string.IsNullOrWhiteSpace(userId) ? string.Empty
				: personnelNames != null && personnelNames.TryGetValue(userId, out var n) ? n : userId;
			string Group(int? groupId) => groupId.HasValue && groupNames != null && groupNames.TryGetValue(groupId.Value, out var g) ? g : string.Empty;
			string When(DateTime? value) => value.HasValue ? value.Value.TimeConverterToString(department) : string.Empty;

			return (projections ?? Enumerable.Empty<RmsRecordSearchProjection>()).Where(p => p != null).Select(p => new RecordsListExportRow
			{
				RecordNumber = p.RecordNumber ?? string.Empty,
				DraftReference = p.DraftReference ?? string.Empty,
				Type = p.RecordType.HasValue ? ((RmsOperationalRecordType)p.RecordType.Value).ToString() : string.Empty,
				DefinitionKey = p.DefinitionKey ?? string.Empty,
				State = ((RmsRecordState)p.State).ToString(),
				Summary = p.DisplaySummary ?? string.Empty,
				OccurredOn = When(p.OccurredOn ?? p.RecordCreatedOn),
				Author = Name(p.AuthorUserId),
				Station = Group(p.StationGroupId),
				CallNumber = p.CallNumber ?? string.Empty,
				CreatedOn = When(p.RecordCreatedOn),
				FinalizedOn = When(p.FinalizedOn),
				RecordId = p.SourceId ?? p.RmsRecordSearchProjectionId
			}).ToList();
		}

		/// <summary>RFC 4180 CSV with a header row and CRLF line endings.</summary>
		public static string ToCsv(IEnumerable<RecordsListExportRow> rows)
		{
			var builder = new StringBuilder();
			builder.Append(string.Join(",", Columns)).Append("\r\n");

			foreach (var row in rows ?? Enumerable.Empty<RecordsListExportRow>())
			{
				builder.Append(string.Join(",", new[]
				{
					Escape(row.RecordNumber), Escape(row.DraftReference), Escape(row.Type), Escape(row.DefinitionKey), Escape(row.State),
					Escape(row.Summary), Escape(row.OccurredOn), Escape(row.Author), Escape(row.Station), Escape(row.CallNumber),
					Escape(row.CreatedOn), Escape(row.FinalizedOn), Escape(row.RecordId)
				})).Append("\r\n");
			}

			return builder.ToString();
		}

		/// <summary>UTF-8 with a byte-order mark so spreadsheet applications open non-ASCII text correctly.</summary>
		public static byte[] ToCsvBytes(IEnumerable<RecordsListExportRow> rows)
		{
			var text = ToCsv(rows);
			var preamble = Encoding.UTF8.GetPreamble();
			var body = Encoding.UTF8.GetBytes(text);
			var bytes = new byte[preamble.Length + body.Length];
			Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
			Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
			return bytes;
		}

		public static string ToJson(IList<RecordsListExportRow> rows, string exportedByUserId)
		{
			return JsonConvert.SerializeObject(new
			{
				format = JsonFormat,
				exportedOn = DateTime.UtcNow,
				exportedByUserId,
				rowCount = rows?.Count ?? 0,
				columns = Columns,
				rows = rows ?? new List<RecordsListExportRow>()
			}, Formatting.Indented);
		}

		/// <summary>
		/// Quotes per RFC 4180 and neutralizes spreadsheet formula injection: a cell that would otherwise start
		/// with =, +, - or @ is prefixed with an apostrophe so it renders as text rather than executing.
		/// </summary>
		public static string Escape(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;

			var text = value;
			if (text[0] == '=' || text[0] == '+' || text[0] == '-' || text[0] == '@' || text[0] == '\t' || text[0] == '\r')
				text = "'" + text;

			if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
				text = "\"" + text.Replace("\"", "\"\"") + "\"";

			return text;
		}
	}
}
