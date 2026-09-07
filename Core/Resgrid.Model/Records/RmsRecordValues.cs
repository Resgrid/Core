using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Stable row identity for one repeating-section row of a draft or revision (RMS plan section 5.2, registry M0159).</summary>
	public class RmsRecordValueGroup : IEntity
	{
		public string RmsRecordValueGroupId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		public int RecordKind { get; set; }
		/// <summary>Null for the working draft; the revision id once copied into an immutable revision.</summary>
		public string RevisionId { get; set; }
		public string RmsRecordDefinitionVersionId { get; set; }
		public string SectionKey { get; set; }
		public int Ordinal { get; set; }
		/// <summary>Client-supplied row key kept so autosave round-trips keep row identity across saves.</summary>
		public string ClientRowKey { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordValueGroupId; }
			set { RmsRecordValueGroupId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsRecordValueGroups";
		[NotMapped] public string IdName => "RmsRecordValueGroupId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One typed value (RMS plan section 5.3): one row per scalar, per repeating-group cell, per selected multi-select
	/// option. Exactly one scalar/reference column group is populated per row; the service guard and the per-dialect
	/// check constraint both enforce it. Locked system definitions never use this table.
	/// </summary>
	public class RmsRecordValue : IEntity
	{
		public string RmsRecordValueId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RecordId { get; set; }
		public int RecordKind { get; set; }
		public string RevisionId { get; set; }
		public string RmsRecordDefinitionVersionId { get; set; }
		public string FieldKey { get; set; }
		public string RmsRecordValueGroupId { get; set; }
		public int Ordinal { get; set; }
		/// <summary>Mirrors RmsRecordFieldDefinition.DataType; a mismatch against the pinned version is rejected.</summary>
		public int ValueType { get; set; }
		public string TextValue { get; set; }
		public string LongTextValue { get; set; }
		public decimal? NumberValue { get; set; }
		public bool? BoolValue { get; set; }
		public DateTime? DateTimeValue { get; set; }
		public int? DateTimeOffsetMinutes { get; set; }
		public long? DurationSeconds { get; set; }
		public string UnitCode { get; set; }
		public decimal? CanonicalNumberValue { get; set; }
		public string CanonicalUnitCode { get; set; }
		public string CurrencyCode { get; set; }
		public string ReferenceType { get; set; }
		public string ReferenceId { get; set; }
		/// <summary>Bounded, server-authored display snapshot of a reference; never client-authored, never a second source of truth.</summary>
		public string ReferenceSnapshotJson { get; set; }
		public string OptionKey { get; set; }
		public bool IsProtected { get; set; }
		public string ProtectedEnvelope { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		/// <summary>True when the field is Protected-classified in its definition version: the ADP seam seals this row (catalog v11) and the sweep targets it.</summary>
		public bool ProtectionRequired { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsRecordValueId; }
			set { RmsRecordValueId = value?.ToString(); }
		}

		[NotMapped] public string TableName => "RmsRecordValues";
		[NotMapped] public string IdName => "RmsRecordValueId";
		[NotMapped] public int IdType => 1;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };

		/// <summary>Sealed under ADP (catalog v11): the envelope holds the packed typed columns and the siblings are null.</summary>
		[NotMapped]
		[JsonIgnore]
		public bool IsSealed => ProtectedDataEnvelope.HasEnvelopePrefix(ProtectedEnvelope);

		/// <summary>The service-layer half of the "exactly one column group" rule.</summary>
		public int PopulatedColumnGroups()
		{
			var count = 0;
			if (TextValue != null) count++;
			if (LongTextValue != null) count++;
			if (NumberValue.HasValue) count++;
			if (BoolValue.HasValue) count++;
			if (DateTimeValue.HasValue) count++;
			if (DurationSeconds.HasValue) count++;
			if (ReferenceId != null) count++;
			if (OptionKey != null) count++;
			return count;
		}
	}

	// ------------------------------------------------------------------------------------------------------
	// Input and hydrated forms
	// ------------------------------------------------------------------------------------------------------

	/// <summary>One posted value. Everything is a string on the wire; the server parses against the pinned field type.</summary>
	/// <summary>
	/// The packed representation of a typed value row under ADP (catalog v11): the sibling typed columns as one JSON
	/// document, sealed into RmsRecordValues.ProtectedEnvelope while the siblings are null. Shared by the write/read seam
	/// (RmsProtectedFields.Values) and the migration engine's PackedJson column kind so both agree byte for byte.
	/// </summary>
	public static class RmsRecordValuePack
	{
		public static readonly IReadOnlyList<string> CarrierColumns = new[]
		{
			"TextValue", "LongTextValue", "NumberValue", "BoolValue", "DateTimeValue", "DateTimeOffsetMinutes", "DurationSeconds",
			"UnitCode", "CanonicalNumberValue", "CanonicalUnitCode", "CurrencyCode", "ReferenceType", "ReferenceId", "ReferenceSnapshotJson", "OptionKey"
		};

		public static IReadOnlyDictionary<string, object> Columns(RmsRecordValue row) => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
		{
			["TextValue"] = row.TextValue, ["LongTextValue"] = row.LongTextValue, ["NumberValue"] = row.NumberValue, ["BoolValue"] = row.BoolValue,
			["DateTimeValue"] = row.DateTimeValue, ["DateTimeOffsetMinutes"] = row.DateTimeOffsetMinutes, ["DurationSeconds"] = row.DurationSeconds,
			["UnitCode"] = row.UnitCode, ["CanonicalNumberValue"] = row.CanonicalNumberValue, ["CanonicalUnitCode"] = row.CanonicalUnitCode,
			["CurrencyCode"] = row.CurrencyCode, ["ReferenceType"] = row.ReferenceType, ["ReferenceId"] = row.ReferenceId,
			["ReferenceSnapshotJson"] = row.ReferenceSnapshotJson, ["OptionKey"] = row.OptionKey
		};

		/// <summary>The row's typed columns as JSON (null when nothing is populated). Numbers and dates are strings in invariant form so scale and kind survive.</summary>
		public static string Pack(RmsRecordValue row) => row == null ? null : PackColumns(Columns(row));

		public static string PackColumns(IReadOnlyDictionary<string, object> columns)
		{
			if (columns == null) return null;
			var packed = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var column in CarrierColumns)
			{
				var key = columns.Keys.FirstOrDefault(k => string.Equals(k, column, StringComparison.OrdinalIgnoreCase));
				var value = key == null ? null : columns[key];
				if (value == null || value is DBNull) continue;
				packed[column] = value switch
				{
					decimal d => d.ToString(CultureInfo.InvariantCulture),
					double d => ((decimal)d).ToString(CultureInfo.InvariantCulture),
					float f => ((decimal)f).ToString(CultureInfo.InvariantCulture),
					bool b => b ? "true" : "false",
					DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
					DateTimeOffset dto => dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
					int i => i.ToString(CultureInfo.InvariantCulture),
					long l => l.ToString(CultureInfo.InvariantCulture),
					short sh => sh.ToString(CultureInfo.InvariantCulture),
					_ => value.ToString()
				};
			}
			return packed.Count == 0 ? null : JsonConvert.SerializeObject(packed);
		}

		/// <summary>Typed CLR values per carrier column (null for anything the pack does not carry), ready for an entity or a Dapper update.</summary>
		public static Dictionary<string, object> UnpackColumns(string json)
		{
			var packed = string.IsNullOrWhiteSpace(json) ? new Dictionary<string, string>() : JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
			var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
			foreach (var column in CarrierColumns)
			{
				packed.TryGetValue(column, out var text);
				result[column] = text == null ? null : column switch
				{
					"NumberValue" or "CanonicalNumberValue" => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture),
					"BoolValue" => (object)(text == "true"),
					"DateTimeValue" => DateTime.SpecifyKind(DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTimeKind.Utc),
					"DateTimeOffsetMinutes" => int.Parse(text, CultureInfo.InvariantCulture),
					"DurationSeconds" => long.Parse(text, CultureInfo.InvariantCulture),
					_ => text
				};
			}
			return result;
		}

		public static void Unpack(RmsRecordValue row, string json)
		{
			if (row == null) return;
			var columns = UnpackColumns(json);
			row.TextValue = (string)columns["TextValue"]; row.LongTextValue = (string)columns["LongTextValue"];
			row.NumberValue = (decimal?)columns["NumberValue"]; row.BoolValue = (bool?)columns["BoolValue"];
			row.DateTimeValue = (DateTime?)columns["DateTimeValue"]; row.DateTimeOffsetMinutes = (int?)columns["DateTimeOffsetMinutes"];
			row.DurationSeconds = (long?)columns["DurationSeconds"]; row.UnitCode = (string)columns["UnitCode"];
			row.CanonicalNumberValue = (decimal?)columns["CanonicalNumberValue"]; row.CanonicalUnitCode = (string)columns["CanonicalUnitCode"];
			row.CurrencyCode = (string)columns["CurrencyCode"]; row.ReferenceType = (string)columns["ReferenceType"]; row.ReferenceId = (string)columns["ReferenceId"];
			row.ReferenceSnapshotJson = (string)columns["ReferenceSnapshotJson"]; row.OptionKey = (string)columns["OptionKey"];
		}

		public static void Clear(RmsRecordValue row)
		{
			if (row == null) return;
			row.TextValue = null; row.LongTextValue = null; row.NumberValue = null; row.BoolValue = null; row.DateTimeValue = null; row.DateTimeOffsetMinutes = null;
			row.DurationSeconds = null; row.UnitCode = null; row.CanonicalNumberValue = null; row.CanonicalUnitCode = null; row.CurrencyCode = null;
			row.ReferenceType = null; row.ReferenceId = null; row.ReferenceSnapshotJson = null; row.OptionKey = null;
		}
	}

	public class RecordValueInput
	{
		public string SectionKey { get; set; }
		public string FieldKey { get; set; }
		/// <summary>Repeating sections: the client's row key (stable across autosaves) and the row's ordinal.</summary>
		public string RowKey { get; set; }
		public int Ordinal { get; set; }
		public string Value { get; set; }
		/// <summary>Multi-select option keys.</summary>
		public List<string> Values { get; set; }
		public string ReferenceType { get; set; }
		public string ReferenceId { get; set; }
		public string UnitCode { get; set; }
		public string CurrencyCode { get; set; }
		public int? OffsetMinutes { get; set; }
	}

	/// <summary>A hydrated cell: the stored row(s) for one field in one row, plus the display text rendered against the pinned schema.</summary>
	public class RecordValueCell
	{
		public string SectionKey { get; set; }
		public string FieldKey { get; set; }
		public string Label { get; set; }
		public RmsFieldType Type { get; set; }
		public RmsFieldClassification Classification { get; set; }
		public string GroupId { get; set; }
		public string RowKey { get; set; }
		public int Ordinal { get; set; }
		/// <summary>Display text (never markup). Null when nothing is stored, "REDACTED" when withheld.</summary>
		public string Display { get; set; }
		/// <summary>Wire form the client can post back unchanged (RecordValueInput.Value).</summary>
		public string Value { get; set; }
		public List<string> Values { get; set; }
		public string ReferenceType { get; set; }
		public string ReferenceId { get; set; }
		public string UnitCode { get; set; }
		public string CurrencyCode { get; set; }
		public int? OffsetMinutes { get; set; }
		public decimal? Number { get; set; }
		public decimal? CanonicalNumber { get; set; }
		public string CanonicalUnitCode { get; set; }
		public bool Withheld { get; set; }
	}

	public class RecordValueRow
	{
		public string GroupId { get; set; }
		public string RowKey { get; set; }
		public int Ordinal { get; set; }
		public List<RecordValueCell> Cells { get; set; } = new List<RecordValueCell>();
		public RecordValueCell Cell(string fieldKey) => Cells.FirstOrDefault(c => string.Equals(c.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase));
	}

	public class RecordValueSectionSet
	{
		public string SectionKey { get; set; }
		public string Label { get; set; }
		public bool Repeating { get; set; }
		public List<RecordValueRow> Rows { get; set; } = new List<RecordValueRow>();
	}

	/// <summary>Everything stored for one Record (draft or revision), shaped by the pinned definition version.</summary>
	public class RecordValueSet
	{
		public string DefinitionKey { get; set; }
		public int DefinitionVersion { get; set; }
		public string DefinitionVersionId { get; set; }
		public List<RecordValueSectionSet> Sections { get; set; } = new List<RecordValueSectionSet>();
		public List<string> WithheldFieldKeys { get; set; } = new List<string>();

		public RecordValueCell Scalar(string fieldKey) => Sections.Where(s => !s.Repeating).SelectMany(s => s.Rows).SelectMany(r => r.Cells).FirstOrDefault(c => string.Equals(c.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase));
		public RecordValueSectionSet Section(string sectionKey) => Sections.FirstOrDefault(s => string.Equals(s.SectionKey, sectionKey, StringComparison.OrdinalIgnoreCase));
		public IEnumerable<RecordValueCell> AllCells() => Sections.SelectMany(s => s.Rows).SelectMany(r => r.Cells);
		public bool IsEmpty => !AllCells().Any(c => c.Display != null);

		/// <summary>Round-trips the set as inputs (what a client posts back on the next save).</summary>
		public List<RecordValueInput> ToInputs()
		{
			var inputs = new List<RecordValueInput>();
			foreach (var section in Sections)
				foreach (var row in section.Rows)
					foreach (var cell in row.Cells.Where(c => !c.Withheld && (c.Value != null || c.Values != null || c.ReferenceId != null)))
						inputs.Add(new RecordValueInput
						{
							SectionKey = section.SectionKey, FieldKey = cell.FieldKey, RowKey = section.Repeating ? row.RowKey ?? row.GroupId : null, Ordinal = row.Ordinal,
							Value = cell.Value, Values = cell.Values, ReferenceType = cell.ReferenceType, ReferenceId = cell.ReferenceId, UnitCode = cell.UnitCode, CurrencyCode = cell.CurrencyCode, OffsetMinutes = cell.OffsetMinutes
						});
			return inputs;
		}
	}

	public class RecordValueIssue
	{
		public string Severity { get; set; } = "error";
		public string SectionKey { get; set; }
		public string FieldKey { get; set; }
		public string RowKey { get; set; }
		public string Code { get; set; }
		public string Message { get; set; }
	}

	public class RecordValueValidation
	{
		public List<RecordValueIssue> Issues { get; set; } = new List<RecordValueIssue>();
		public bool IsValid => Issues.All(i => i.Severity != "error");
	}

	/// <summary>The outcome of evaluating a version's rules over a value set: what is visible, what is required.</summary>
	public class RecordRuleEvaluation
	{
		public HashSet<string> HiddenSectionKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Fields hidden regardless of row: scalar fields, and repeating-section fields whose rules only look at scalars.</summary>
		public HashSet<string> HiddenFieldKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public HashSet<string> RequiredFieldKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Per-row outcomes for repeating sections (a rule may look at its own row); keyed "section|rowKey".</summary>
		public Dictionary<string, RecordRowRuleEvaluation> Rows { get; } = new Dictionary<string, RecordRowRuleEvaluation>(StringComparer.OrdinalIgnoreCase);

		public static string RowKey(string sectionKey, string rowKey) => (sectionKey ?? string.Empty) + "|" + (rowKey ?? string.Empty);

		public RecordRowRuleEvaluation Row(string sectionKey, string rowKey)
		{
			var key = RowKey(sectionKey, rowKey);
			if (!Rows.TryGetValue(key, out var row)) Rows[key] = row = new RecordRowRuleEvaluation { SectionKey = sectionKey, RowKey = rowKey };
			return row;
		}

		public bool IsHidden(string sectionKey, string rowKey, string fieldKey) =>
			HiddenSectionKeys.Contains(sectionKey ?? string.Empty) || HiddenFieldKeys.Contains(fieldKey)
			|| rowKey != null && Rows.TryGetValue(RowKey(sectionKey, rowKey), out var row) && row.HiddenFieldKeys.Contains(fieldKey);

		public bool IsRequired(string sectionKey, string rowKey, string fieldKey) =>
			!IsHidden(sectionKey, rowKey, fieldKey) && (RequiredFieldKeys.Contains(fieldKey)
			|| rowKey != null && Rows.TryGetValue(RowKey(sectionKey, rowKey), out var row) && row.RequiredFieldKeys.Contains(fieldKey));
	}

	public class RecordRowRuleEvaluation
	{
		public string SectionKey { get; set; }
		public string RowKey { get; set; }
		public HashSet<string> HiddenFieldKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		public HashSet<string> RequiredFieldKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	// ------------------------------------------------------------------------------------------------------
	// Units, currencies, countries (RMS-1C, decision 22: originals round-trip, canonical values derive)
	// ------------------------------------------------------------------------------------------------------

	public sealed class RmsUnit
	{
		public RmsUnit(string code, string family, string label, string system, decimal toCanonical, decimal offset = 0m)
		{
			Code = code; Family = family; Label = label; System = system; ToCanonical = toCanonical; Offset = offset;
		}

		public string Code { get; }
		public string Family { get; }
		public string Label { get; }
		/// <summary>metric | customary | both</summary>
		public string System { get; }
		/// <summary>Multiplier into the family's canonical unit (after the offset for temperature).</summary>
		public decimal ToCanonical { get; }
		public decimal Offset { get; }
	}

	/// <summary>
	/// Bounded unit catalog. Canonical units are metric (m, L, ha, kg, C); U.S. customary values are stored as entered
	/// with their unit and a derived canonical value for comparison, never silently converted (RMS plan section 4.1).
	/// </summary>
	public static class RmsUnits
	{
		public static readonly IReadOnlyList<RmsUnit> All = new List<RmsUnit>
		{
			new RmsUnit("m", "length", "metres", "metric", 1m),
			new RmsUnit("km", "length", "kilometres", "metric", 1000m),
			new RmsUnit("ft", "length", "feet", "customary", 0.3048m),
			new RmsUnit("mi", "length", "miles", "customary", 1609.344m),
			new RmsUnit("L", "volume", "litres", "metric", 1m),
			new RmsUnit("gal", "volume", "U.S. gallons", "customary", 3.785411784m),
			new RmsUnit("ha", "area", "hectares", "metric", 1m),
			new RmsUnit("ac", "area", "acres", "customary", 0.40468564224m),
			new RmsUnit("kg", "mass", "kilograms", "metric", 1m),
			new RmsUnit("lb", "mass", "pounds", "customary", 0.45359237m),
			new RmsUnit("C", "temperature", "degrees Celsius", "metric", 1m),
			new RmsUnit("F", "temperature", "degrees Fahrenheit", "customary", 5m / 9m, -32m),
			new RmsUnit("h", "time", "hours", "both", 60m),
			new RmsUnit("min", "time", "minutes", "both", 1m),
			new RmsUnit("count", "count", "count", "both", 1m)
		};

		public static readonly IReadOnlyDictionary<string, string> CanonicalByFamily = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["length"] = "m", ["volume"] = "L", ["area"] = "ha", ["mass"] = "kg", ["temperature"] = "C", ["time"] = "min", ["count"] = "count"
		};

		public static RmsUnit Find(string code) => All.FirstOrDefault(u => string.Equals(u.Code, code, StringComparison.Ordinal));

		public static IEnumerable<RmsUnit> ForFamily(string family) => All.Where(u => string.Equals(u.Family, family, StringComparison.OrdinalIgnoreCase));

		/// <summary>Converts into the family's canonical unit; null when the unit is unknown.</summary>
		public static (decimal Value, string Unit)? Canonicalize(decimal value, string unitCode)
		{
			var unit = Find(unitCode);
			if (unit == null) return null;
			var canonical = CanonicalByFamily[unit.Family];
			if (unit.Code == canonical) return (value, canonical);
			return (decimal.Round((value + unit.Offset) * unit.ToCanonical, 6), canonical);
		}

		/// <summary>Converts a canonical value back into <paramref name="unitCode"/> (display in the reader's system).</summary>
		public static decimal? FromCanonical(decimal canonicalValue, string unitCode)
		{
			var unit = Find(unitCode);
			if (unit == null) return null;
			var canonical = CanonicalByFamily[unit.Family];
			if (unit.Code == canonical) return canonicalValue;
			return decimal.Round(canonicalValue / unit.ToCanonical - unit.Offset, 6);
		}

		/// <summary>The unit a measurement system prefers for a family (customary for U.S., metric elsewhere).</summary>
		public static string PreferredUnit(string family, string measurementSystem)
		{
			var customary = string.Equals(measurementSystem, "customary", StringComparison.OrdinalIgnoreCase);
			var candidate = ForFamily(family).FirstOrDefault(u => customary ? u.System == "customary" : u.System == "metric");
			return candidate?.Code ?? (CanonicalByFamily.TryGetValue(family ?? string.Empty, out var c) ? c : null);
		}
	}

	public static class RmsCurrencies
	{
		/// <summary>ISO 4217 codes accepted at minimum (RMS plan section 4.1). No foreign-exchange conversion anywhere.</summary>
		public static readonly IReadOnlyList<string> Supported = new List<string> { "USD", "CAD", "EUR", "GBP", "AUD", "MXN" };
		public static bool IsSupported(string code) => code != null && Supported.Contains(code.ToUpperInvariant());
		public static string Format(decimal amount, string code) => amount.ToString("N2", CultureInfo.InvariantCulture) + " " + (code ?? string.Empty).ToUpperInvariant();
	}

	/// <summary>ISO 3166-2 style country/subdivision codes for the U.S. and Canada (RMS-1C profile-aware addresses).</summary>
	public static class RmsCountrySubdivisions
	{
		public static readonly IReadOnlyDictionary<string, string> UnitedStates = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas", ["CA"] = "California", ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware",
			["DC"] = "District of Columbia", ["FL"] = "Florida", ["GA"] = "Georgia", ["HI"] = "Hawaii", ["ID"] = "Idaho", ["IL"] = "Illinois", ["IN"] = "Indiana", ["IA"] = "Iowa",
			["KS"] = "Kansas", ["KY"] = "Kentucky", ["LA"] = "Louisiana", ["ME"] = "Maine", ["MD"] = "Maryland", ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota",
			["MS"] = "Mississippi", ["MO"] = "Missouri", ["MT"] = "Montana", ["NE"] = "Nebraska", ["NV"] = "Nevada", ["NH"] = "New Hampshire", ["NJ"] = "New Jersey", ["NM"] = "New Mexico",
			["NY"] = "New York", ["NC"] = "North Carolina", ["ND"] = "North Dakota", ["OH"] = "Ohio", ["OK"] = "Oklahoma", ["OR"] = "Oregon", ["PA"] = "Pennsylvania", ["RI"] = "Rhode Island",
			["SC"] = "South Carolina", ["SD"] = "South Dakota", ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah", ["VT"] = "Vermont", ["VA"] = "Virginia", ["WA"] = "Washington",
			["WV"] = "West Virginia", ["WI"] = "Wisconsin", ["WY"] = "Wyoming", ["PR"] = "Puerto Rico", ["GU"] = "Guam", ["VI"] = "U.S. Virgin Islands", ["AS"] = "American Samoa", ["MP"] = "Northern Mariana Islands"
		};

		public static readonly IReadOnlyDictionary<string, string> Canada = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["AB"] = "Alberta", ["BC"] = "British Columbia", ["MB"] = "Manitoba", ["NB"] = "New Brunswick", ["NL"] = "Newfoundland and Labrador", ["NS"] = "Nova Scotia",
			["NT"] = "Northwest Territories", ["NU"] = "Nunavut", ["ON"] = "Ontario", ["PE"] = "Prince Edward Island", ["QC"] = "Quebec", ["SK"] = "Saskatchewan", ["YT"] = "Yukon"
		};

		public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByCountry = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
		{
			["US"] = UnitedStates, ["CA"] = Canada
		};

		/// <summary>"US-CA" -> valid; a country with no bounded list ("MX-...") is accepted as an opaque code of the form CC-XXX.</summary>
		public static bool IsValid(string code)
		{
			if (string.IsNullOrWhiteSpace(code)) return false;
			var parts = code.Split('-');
			if (parts.Length == 1) return parts[0].Length == 2 && parts[0].All(char.IsLetter);
			if (parts.Length != 2 || parts[0].Length != 2) return false;
			return ByCountry.TryGetValue(parts[0], out var list) ? list.ContainsKey(parts[1]) : parts[1].Length >= 1 && parts[1].Length <= 3;
		}

		public static string Label(string code)
		{
			if (string.IsNullOrWhiteSpace(code)) return null;
			var parts = code.Split('-');
			var country = parts[0] == "US" ? "United States" : parts[0] == "CA" ? "Canada" : parts[0];
			if (parts.Length == 1) return country;
			return ByCountry.TryGetValue(parts[0], out var list) && list.TryGetValue(parts[1], out var name) ? name + ", " + country : parts[1] + ", " + country;
		}
	}
}
