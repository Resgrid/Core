using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Resgrid.Model;
using Resgrid.Model.Reporting;

namespace Resgrid.Services.Reporting
{
	/// <summary>
	/// Incident CSV export and the NFIRS/NEMSIS field-mapping "readiness" layer. Each profile declares
	/// its standardized field set; fields Resgrid captures are mapped, fields it does not are emitted as
	/// empty cells (a "gap") and reported via <see cref="GetUnmappedRequiredFields"/>. The output header
	/// is always the full profile schema so the file is structurally submission-ready.
	/// </summary>
	public static class IncidentExport
	{
		private sealed class ExportField
		{
			public string Name { get; }
			public Func<Call, string> Map { get; } // null => not captured by Resgrid (a gap)
			public bool Required { get; }

			public ExportField(string name, Func<Call, string> map, bool required = false)
			{
				Name = name;
				Map = map;
				Required = required;
			}

			public bool IsGap => Map == null;
		}

		public static byte[] BuildCsv(ExportProfile profile, IEnumerable<Call> calls)
		{
			var fields = GetFields(profile);
			var sb = new StringBuilder();

			sb.AppendLine(string.Join(",", fields.Select(f => Escape(f.Name))));

			foreach (var call in calls ?? Enumerable.Empty<Call>())
				sb.AppendLine(string.Join(",", fields.Select(f => Escape(f.Map == null ? string.Empty : (f.Map(call) ?? string.Empty)))));

			return new UTF8Encoding(false).GetBytes(sb.ToString());
		}

		public static IReadOnlyList<string> GetUnmappedRequiredFields(ExportProfile profile)
			=> GetFields(profile).Where(f => f.Required && f.IsGap).Select(f => f.Name).ToList();

		private static IReadOnlyList<ExportField> GetFields(ExportProfile profile)
		{
			switch (profile)
			{
				case ExportProfile.Nfirs: return Nfirs;
				case ExportProfile.Nemsis: return Nemsis;
				default: return Generic;
			}
		}

		// ----- Generic Resgrid incident columns (no required/standardized gaps) -----
		private static readonly IReadOnlyList<ExportField> Generic = new List<ExportField>
		{
			new ExportField("CallId", c => Int(c.CallId)),
			new ExportField("Number", c => c.Number),
			new ExportField("IncidentNumber", c => c.IncidentNumber),
			new ExportField("Type", c => c.Type),
			new ExportField("Priority", c => Int(c.Priority)),
			new ExportField("State", c => Int(c.State)),
			new ExportField("Name", c => c.Name),
			new ExportField("NatureOfCall", c => c.NatureOfCall),
			new ExportField("Address", c => c.Address),
			new ExportField("LoggedOnUtc", c => Utc(c.LoggedOn)),
			new ExportField("DispatchOnUtc", c => Utc(c.DispatchOn)),
			new ExportField("ClosedOnUtc", c => Utc(c.ClosedOn)),
			new ExportField("CallSource", c => Int(c.CallSource)),
			new ExportField("DispatchCount", c => Int(c.DispatchCount)),
		};

		// ----- NFIRS 5.0 (Basic module) representative field set (fire) -----
		private static readonly IReadOnlyList<ExportField> Nfirs = new List<ExportField>
		{
			new ExportField("FDID", null, required: true),                          // department's NFIRS Fire Dept ID — not captured
			new ExportField("IncidentDate", c => Utc(c.LoggedOn), required: true),
			new ExportField("Station", null),
			new ExportField("IncidentNumber", c => PreferIncidentNumber(c), required: true),
			new ExportField("ExposureNumber", c => "000"),
			new ExportField("IncidentTypeCode", null, required: true),              // Resgrid Type is free text, not an NFIRS code
			new ExportField("AlarmDateTime", c => Utc(c.LoggedOn), required: true),
			new ExportField("ArrivalDateTime", null),                              // first on-scene — state-log derived (5b)
			new ExportField("LastUnitClearedDateTime", c => Utc(c.ClosedOn)),
			new ExportField("LocationAddress", c => c.Address, required: true),
			new ExportField("IncidentName", c => c.Name),
			new ExportField("NatureOfCall", c => c.NatureOfCall),
			new ExportField("AidGivenOrReceived", null),
			new ExportField("ActionsTaken", null),
			new ExportField("PropertyUse", null),
		};

		// ----- NEMSIS v3 representative field set (EMS) -----
		private static readonly IReadOnlyList<ExportField> Nemsis = new List<ExportField>
		{
			new ExportField("PatientCareReportNumber", null, required: true),       // eRecord.01
			new ExportField("EMSAgencyNumber", null, required: true),               // eResponse.03
			new ExportField("IncidentNumber", c => PreferIncidentNumber(c), required: true), // eResponse.04
			new ExportField("PSAPCallDateTime", c => Utc(c.LoggedOn), required: true),       // eTimes.01
			new ExportField("UnitNotifiedByDispatchDateTime", c => Utc(c.DispatchOn)),       // eTimes.03
			new ExportField("UnitArrivedOnSceneDateTime", null),                   // eTimes.06 — state-log derived (5b)
			new ExportField("UnitLeftSceneDateTime", null),                        // eTimes.09
			new ExportField("PatientArrivedAtDestinationDateTime", null),          // eTimes.11
			new ExportField("SceneAddress", c => c.Address),                       // eScene
			new ExportField("IncidentNature", c => c.NatureOfCall),
			new ExportField("Disposition", null),                                  // eDisposition
		};

		private static string PreferIncidentNumber(Call c)
			=> string.IsNullOrWhiteSpace(c.IncidentNumber) ? c.Number : c.IncidentNumber;

		private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

		private static string Utc(DateTime value)
			=> value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

		private static string Utc(DateTime? value)
			=> value.HasValue ? Utc(value.Value) : string.Empty;

		private static string Escape(string value)
		{
			value ??= string.Empty;
			if (value.IndexOf('"') >= 0 || value.IndexOf(',') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
				return "\"" + value.Replace("\"", "\"\"") + "\"";
			return value;
		}
	}
}
