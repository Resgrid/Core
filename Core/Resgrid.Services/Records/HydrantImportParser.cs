using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Services.Records
{
	/// <summary>Validates the complete import before any writes; JSON and CSV share the same field rules.</summary>
	public static class HydrantImportParser
	{
		public const int MaximumBytes = 10 * 1024 * 1024;
		public const int MaximumRows = 20000;
		public const string JsonExample = "[\n  {\n    \"number\": \"H-101\",\n    \"latitude\": 45.5,\n    \"longitude\": -122.6,\n    \"type\": \"DryBarrel\",\n    \"address\": \"1 River Rd\",\n    \"main_size\": 8,\n    \"flow_gpm\": 1100,\n    \"owner\": \"City\"\n  }\n]";
		public const string CsvExample = "number,latitude,longitude,type,address,main_size,flow_gpm,owner\nH-101,45.5,-122.6,DryBarrel,1 River Rd,8,1100,City\n";
		/// <summary>The CSV columns that map to hydrant fields (after <see cref="CanonicalHeader"/>); the default order for a file without a header.</summary>
		private static readonly string[] CsvColumns = { "number", "latitude", "longitude", "type", "address", "main_size", "flow_gpm", "owner" };
		/// <summary>Every column the CSV reads: <see cref="CsvColumns"/> plus flow_class, which only another system's flow colour column maps to.</summary>
		private static readonly string[] KnownCsvColumns = CsvColumns.Concat(new[] { "flow_class", "in_service" }).ToArray();

		public sealed class Input
		{
			[Required, JsonProperty("number")] public string Number { get; set; }
			[Required, JsonProperty("latitude")] public decimal Latitude { get; set; }
			[Required, JsonProperty("longitude")] public decimal Longitude { get; set; }
			[JsonProperty("type")] public RmsHydrantType? Type { get; set; }
			[JsonProperty("address")] public string Address { get; set; }
			[JsonProperty("main_size")] public decimal? MainSize { get; set; }
			[JsonProperty("flow_gpm")] public int? FlowGpm { get; set; }
			[JsonProperty("owner")] public string Owner { get; set; }
		}

		public sealed class Batch
		{
			public HydrantImportResult Result { get; } = new HydrantImportResult();
			/// <summary>InService: the service state another system's status column gives (null = none, leave the hydrant's state as it is).</summary>
			public List<(HydrantImportRow Row, RmsHydrant Hydrant, bool? InService)> Rows { get; } = new List<(HydrantImportRow, RmsHydrant, bool?)>();
			/// <summary>Values another system's column held that could not be read, keyed by column and value (case-insensitive).</summary>
			internal Dictionary<string, HydrantImportUnmappedValue> Unmapped { get; } = new Dictionary<string, HydrantImportUnmappedValue>(StringComparer.OrdinalIgnoreCase);

			internal void AddUnmapped(string column, string value)
			{
				if (Unmapped.TryGetValue(column + "\n" + value, out var entry)) entry.Rows++;
				else Unmapped[column + "\n" + value] = new HydrantImportUnmappedValue { Column = column, Value = value, Rows = 1 };
			}
		}

		public static Batch Parse(string content, string format)
		{
			if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Choose a JSON or CSV file, or paste its contents before importing.");
			if (Encoding.UTF8.GetByteCount(content) > MaximumBytes) throw new ArgumentException("The import exceeds 10 MB. Split it into smaller files and try again.");
			var batch = new Batch();
			var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)) ParseJson(content.TrimStart('\uFEFF'), batch, numbers);
			else if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)) ParseCsv(content.TrimStart('\uFEFF'), batch, numbers);
			else throw new ArgumentException("Select JSON or CSV as the import format.");
			if (batch.Result.RowsRead == 0) throw new ArgumentException("The import has no hydrants. Add at least one row using the example.");
			batch.Result.ValidationFailed = batch.Result.Rejected.Count > 0;
			batch.Result.UnmappedValues.AddRange(batch.Unmapped.Values.OrderByDescending(u => u.Rows).ThenBy(u => u.Column).ThenBy(u => u.Value, StringComparer.OrdinalIgnoreCase));
			return batch;
		}

		private static void ParseJson(string content, Batch batch, HashSet<string> numbers)
		{
			try
			{
				using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 16 });
				if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("JSON must be an array of hydrant objects: [ { ... } ]. Use the downloadable example.");
				if (document.RootElement.GetArrayLength() > MaximumRows) throw TooManyRows();
				foreach (var item in document.RootElement.EnumerateArray())
				{
					var row = new HydrantImportRow { Line = ++batch.Result.RowsRead };
					if (item.ValueKind == JsonValueKind.Object)
					{
						var number = item.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, "number", StringComparison.OrdinalIgnoreCase)).Value;
						if (number.ValueKind == JsonValueKind.String) row.HydrantNumber = number.GetString();
					}
					try { Add(JsonInput.Read<Input>(item.GetRawText(), "Row " + row.Line), row, batch, numbers); }
					catch (Exception ex) when (ex is JsonInputException || ex is ArgumentException) { Reject(row, ex.Message, batch); }
				}
			}
			catch (System.Text.Json.JsonException ex)
			{
				throw new ArgumentException($"Invalid JSON at line {ex.LineNumber + 1}, byte {ex.BytePositionInLine + 1}. Check double quotes, commas and brackets. Comments and trailing commas are not allowed. No hydrants were saved.");
			}
		}

		private static void ParseCsv(string content, Batch batch, HashSet<string> numbers)
		{
			using var parser = new TextFieldParser(new StringReader(content)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
			parser.SetDelimiters(",");
			var header = CsvColumns;
			var first = true;
			// Header text of a type/flow column that came in under another system's name (hydrant_type, flow_rate...); null = absent or our own name.
			string foreignType = null, foreignFlow = null, flowClassColumn = null, statusColumn = null;
			try
			{
				while (!parser.EndOfData)
				{
					var line = (int)parser.LineNumber;
					var cells = parser.ReadFields();
					if (first)
					{
						first = false;
						var named = cells.Select(NormalizeHeader).ToArray();
						var candidate = named.Select(CanonicalHeader).ToArray();
						if (candidate.Contains("number") || candidate.Contains("latitude") || candidate.Contains("longitude"))
						{
							var duplicate = candidate.Where(KnownCsvColumns.Contains).GroupBy(c => c).FirstOrDefault(g => g.Count() > 1);
							if (duplicate != null)
								throw new ArgumentException($"The CSV has more than one {duplicate.Key} column ({string.Join(", ", cells.Where((_, i) => candidate[i] == duplicate.Key).Select(c => c.Trim()))}). Keep one column for each field.");
							if (!new[] { "number", "latitude", "longitude" }.All(candidate.Contains)) throw new ArgumentException("The CSV header must include number, latitude and longitude.");
							// Exports from other systems carry extra columns (status, zone, inspection dates...). They are skipped and reported
							// rather than failing the file; they stay in the header so every row's cell count still lines up.
							batch.Result.IgnoredColumns.AddRange(cells.Where((c, i) => !KnownCsvColumns.Contains(candidate[i]) && !string.IsNullOrWhiteSpace(c))
								.Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
							header = candidate;
							// A column under another system's name holds that system's vocabulary, so a value we cannot read is imported as a
							// default and reported instead of rejecting the file. Columns under our documented names stay strict.
							foreignType = ForeignHeader(cells, named, candidate, "type");
							foreignFlow = ForeignHeader(cells, named, candidate, "flow_gpm");
							var flowClassIndex = Array.IndexOf(candidate, "flow_class");
							flowClassColumn = flowClassIndex >= 0 ? cells[flowClassIndex].Trim() : null;
							var statusIndex = Array.IndexOf(candidate, "in_service");
							statusColumn = statusIndex >= 0 ? cells[statusIndex].Trim() : null;
							continue;
						}
					}
					if (++batch.Result.RowsRead > MaximumRows) throw TooManyRows();
					var row = new HydrantImportRow { Line = line };
					try
					{
						if (cells.Length != header.Length) throw new ArgumentException($"Expected {header.Length} columns but found {cells.Length}. Use commas between fields and double quotes around values containing commas or line breaks.");
						string Cell(string name) { var i = Array.IndexOf(header, name); return i < 0 ? null : cells[i]; }
						decimal? Number(string name, bool required = false)
						{
							var value = Cell(name);
							if (!required && string.IsNullOrWhiteSpace(value)) return null;
							if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) throw new ArgumentException(name + ": enter a number using a decimal point, for example 45.5.");
							return number;
						}
						row.HydrantNumber = Cell("number");
						var flow = Cell("flow_gpm");
						int? flowGpm = null;
						if (foreignFlow != null) flowGpm = ReadForeignFlow(flow, foreignFlow, batch);
						else if (!string.IsNullOrWhiteSpace(flow))
						{
							if (!int.TryParse(flow, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) throw new ArgumentException("flow_gpm: enter a whole number between 0 and 2147483647, without decimals.");
							flowGpm = value;
						}
						var flowClass = flowClassColumn != null ? ReadForeignFlowClass(Cell("flow_class"), flowClassColumn, batch) : RmsHydrantFlowClass.Unknown;
						var inService = statusColumn != null ? ReadForeignStatus(Cell("in_service"), statusColumn, batch) : null;
						// The status as the other system wrote it is the out-of-service reason, so the hydrant shows why it was taken out.
						var outOfServiceReason = inService == false ? statusColumn + ": " + Cell("in_service").Trim() : null;
						Add(new Input { Number = row.HydrantNumber, Latitude = Number("latitude", true).Value, Longitude = Number("longitude", true).Value,
							Type = foreignType != null ? ReadForeignType(Cell("type"), foreignType, batch) : ParseType(Cell("type")), Address = Cell("address"), MainSize = Number("main_size"), FlowGpm = flowGpm, Owner = Cell("owner") },
							row, batch, numbers, flowClass, inService, outOfServiceReason);
					}
					catch (ArgumentException ex) { Reject(row, ex.Message, batch); }
				}
			}
			catch (MalformedLineException) { throw new ArgumentException($"CSV line {parser.ErrorLineNumber} has unmatched quotes. Enclose each quoted value in double quotes. No hydrants were saved."); }
		}

		private static string NormalizeHeader(string value) => value.Trim().ToLowerInvariant().Replace(" ", "_");

		private static string CanonicalHeader(string name)
		{
			return name switch { "hydrant_number" or "id" => "number", "lat" => "latitude", "lon" or "lng" => "longitude", "location_address" => "address", "hydrant_type" => "type",
				"main_size_inches" or "main" => "main_size", "flow" or "flow_rate" => "flow_gpm", "owner_name" => "owner",
				"flow_class" or "flow_rating_color" or "flow_rating_colour" or "flow_color" or "flow_colour" or "cap_color" or "cap_colour" or "bonnet_color" or "bonnet_colour" => "flow_class",
				"in_service" or "status" or "hydrant_status" or "service_status" or "operational_status" => "in_service",
				_ => name };
		}

		/// <summary>The header as written when <paramref name="field"/> came in under an alias rather than its documented name; otherwise null.</summary>
		private static string ForeignHeader(string[] cells, string[] named, string[] candidate, string field)
		{
			var i = Array.IndexOf(candidate, field);
			return i >= 0 && named[i] != field ? cells[i].Trim() : null;
		}

		/// <summary>Words other systems use for "no value"; a foreign column holding one is read as blank.</summary>
		private static readonly HashSet<string> NoValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "-", "--", "n/a", "na", "none", "null", "unknown" };

		/// <summary>Type names other systems use (letters only, lower case). "Dry hydrant" is the fire-service term for an unpressurized drafting pipe.</summary>
		private static readonly Dictionary<string, RmsHydrantType> TypeSynonyms = new Dictionary<string, RmsHydrantType>(StringComparer.Ordinal)
		{
			["dry"] = RmsHydrantType.DryBarrel, ["wet"] = RmsHydrantType.WetBarrel,
			["tank"] = RmsHydrantType.Cistern, ["watertank"] = RmsHydrantType.Cistern, ["storagetank"] = RmsHydrantType.Cistern, ["undergroundtank"] = RmsHydrantType.Cistern,
			["dryhydrant"] = RmsHydrantType.DraftingSite, ["draft"] = RmsHydrantType.DraftingSite, ["drafting"] = RmsHydrantType.DraftingSite, ["drafthydrant"] = RmsHydrantType.DraftingSite,
			["draftsite"] = RmsHydrantType.DraftingSite, ["draftpoint"] = RmsHydrantType.DraftingSite, ["draftingpoint"] = RmsHydrantType.DraftingSite,
			["staticsource"] = RmsHydrantType.DraftingSite, ["staticwater"] = RmsHydrantType.DraftingSite, ["staticwatersource"] = RmsHydrantType.DraftingSite,
			["pond"] = RmsHydrantType.DraftingSite, ["lake"] = RmsHydrantType.DraftingSite, ["river"] = RmsHydrantType.DraftingSite
		};

		private static bool TryParseType(string text, out RmsHydrantType type)
		{
			// A number is read as a number before separators are stripped from names: "-2" or "0.5" would otherwise become the
			// defined values 2 and 5.
			if (decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
			{
				type = default;
				if (number != decimal.Truncate(number) || number < 0 || number > int.MaxValue)
					return false;

				type = (RmsHydrantType)(int)number;
				return Enum.IsDefined(type);
			}

			var value = new string(text.Trim().ToLowerInvariant().Where(c => c != ' ' && c != '-' && c != '_' && c != '.').ToArray());
			// "Dry Barrel Hydrant", "Wet Hydrant": a trailing "hydrant" adds nothing once the explicit names ("dry hydrant") have been tried.
			foreach (var name in new[] { value, value.EndsWith("hydrant", StringComparison.Ordinal) ? value.Substring(0, value.Length - 7) : null })
			{
				if (string.IsNullOrEmpty(name)) continue;
				if (TypeSynonyms.TryGetValue(name, out type)) return true;
				// Letters/digits only, so "DryBarrel,WetBarrel" is not read as the flags combination 1|2 = Standpipe.
				if (name.All(char.IsLetterOrDigit) && Enum.TryParse(name, true, out type) && Enum.IsDefined(type)) return true;
			}
			type = default;
			return false;
		}

		private static RmsHydrantType ParseType(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return RmsHydrantType.DryBarrel;
			if (TryParseType(text, out var type)) return type;
			throw new ArgumentException("type: use DryBarrel, WetBarrel, Standpipe, Cistern, DraftingSite or Other (or 1–6).");
		}

		/// <summary>A type from another system's column: blank is the default type, an unknown name is imported as Other and reported.</summary>
		private static RmsHydrantType ReadForeignType(string text, string column, Batch batch)
		{
			if (string.IsNullOrWhiteSpace(text) || NoValue.Contains(text.Trim())) return RmsHydrantType.DryBarrel;
			if (TryParseType(text, out var type)) return type;
			batch.AddUnmapped(column, text.Trim());
			return RmsHydrantType.Other;
		}

		private const decimal LitresPerUsGallon = 3.785411784m;

		/// <summary>Unit suffixes a flow value may carry (spaces removed, lower case) and how many of the unit make one US gallon. Longest first.</summary>
		private static readonly (string Suffix, decimal PerGallon)[] FlowUnits =
		{
			("usgal/min", 1m), ("gal/min", 1m), ("usgpm", 1m), ("gpm", 1m), ("l/min", LitresPerUsGallon), ("lpm", LitresPerUsGallon), ("l/m", LitresPerUsGallon)
		};

		private static readonly Regex ThousandsSeparated = new Regex(@"^\d{1,3}(,\d{3})+(\.\d+)?$", RegexOptions.CultureInvariant);

		/// <summary>
		/// A flow from another system's column: gallons per minute unless the value names litres per minute; decimals are rounded and
		/// "1,250" thousands separators accepted. A value that is not a nonnegative flow is imported as blank and reported.
		/// </summary>
		private static int? ReadForeignFlow(string text, string column, Batch batch)
		{
			if (string.IsNullOrWhiteSpace(text) || NoValue.Contains(text.Trim())) return null;
			var value = text.Trim().ToLowerInvariant().Replace(" ", "");
			var perGallon = 1m;
			foreach (var (suffix, units) in FlowUnits)
				if (value.EndsWith(suffix, StringComparison.Ordinal)) { value = value.Substring(0, value.Length - suffix.Length); perGallon = units; break; }
			if (ThousandsSeparated.IsMatch(value)) value = value.Replace(",", "");
			if (decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
			{
				var gpm = decimal.Round(number / perGallon, 0, MidpointRounding.AwayFromZero);
				if (gpm <= int.MaxValue) return (int)gpm;
			}
			batch.AddUnmapped(column, text.Trim());
			return null;
		}

		/// <summary>NFPA 291 bonnet/cap colour names and class letters (letters only, lower case).</summary>
		private static readonly Dictionary<string, RmsHydrantFlowClass> FlowClassNames = new Dictionary<string, RmsHydrantFlowClass>(StringComparer.Ordinal)
		{
			["lightblue"] = RmsHydrantFlowClass.AA, ["ltblue"] = RmsHydrantFlowClass.AA, ["skyblue"] = RmsHydrantFlowClass.AA, ["blue"] = RmsHydrantFlowClass.AA,
			["aa"] = RmsHydrantFlowClass.AA, ["classaa"] = RmsHydrantFlowClass.AA,
			["green"] = RmsHydrantFlowClass.A, ["a"] = RmsHydrantFlowClass.A, ["classa"] = RmsHydrantFlowClass.A,
			["orange"] = RmsHydrantFlowClass.B, ["b"] = RmsHydrantFlowClass.B, ["classb"] = RmsHydrantFlowClass.B,
			["red"] = RmsHydrantFlowClass.C, ["c"] = RmsHydrantFlowClass.C, ["classc"] = RmsHydrantFlowClass.C
		};

		private static readonly Regex HexColour = new Regex(@"^#([0-9a-f]{3}|[0-9a-f]{6})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

		/// <summary>
		/// A flow class from another system's colour column: an NFPA 291 colour name, class letter or #hex colour. Blank is no class; anything
		/// else (black, yellow...) is no class and reported. It only applies on import while the hydrant has no flow to classify.
		/// </summary>
		private static RmsHydrantFlowClass ReadForeignFlowClass(string text, string column, Batch batch)
		{
			if (string.IsNullOrWhiteSpace(text) || NoValue.Contains(text.Trim())) return RmsHydrantFlowClass.Unknown;
			var name = new string(text.Trim().ToLowerInvariant().Where(c => c != ' ' && c != '-' && c != '_' && c != '.').ToArray());
			if (FlowClassNames.TryGetValue(name, out var flowClass)) return flowClass;
			if (TryClassifyColour(text.Trim(), out flowClass)) return flowClass;
			batch.AddUnmapped(column, text.Trim());
			return RmsHydrantFlowClass.Unknown;
		}

		/// <summary>Classifies a #hex colour by hue: red, orange, green and blue are the NFPA 291 cap colours. Greys, yellows and purples are not.</summary>
		private static bool TryClassifyColour(string text, out RmsHydrantFlowClass flowClass)
		{
			flowClass = RmsHydrantFlowClass.Unknown;
			if (!HexColour.IsMatch(text)) return false;
			var hex = text.Substring(1);
			if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
			var rgb = Enumerable.Range(0, 3).Select(i => int.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0).ToArray();
			double max = rgb.Max(), min = rgb.Min(), delta = max - min;
			if (delta < 0.15 || max < 0.25) return false;
			var hue = max == rgb[0] ? 60 * (((rgb[1] - rgb[2]) / delta + 6) % 6)
				: max == rgb[1] ? 60 * ((rgb[2] - rgb[0]) / delta + 2)
				: 60 * ((rgb[0] - rgb[1]) / delta + 4);
			// Green stops at 175 so the map's own class A green (#1ab394, ~168) and class AA light blue (#23c6c8, ~181) stay apart.
			flowClass = hue < 15 || hue >= 330 ? RmsHydrantFlowClass.C
				: hue < 50 ? RmsHydrantFlowClass.B
				: hue >= 75 && hue < 175 ? RmsHydrantFlowClass.A
				: hue >= 175 && hue < 260 ? RmsHydrantFlowClass.AA
				: RmsHydrantFlowClass.Unknown;
			return flowClass != RmsHydrantFlowClass.Unknown;
		}

		/// <summary>Service-state words other systems use (letters and digits only, lower case): true = in service, false = out of service.</summary>
		private static readonly Dictionary<string, bool> StatusWords = new Dictionary<string, bool>(StringComparer.Ordinal)
		{
			["inservice"] = true, ["active"] = true, ["operational"] = true, ["available"] = true, ["working"] = true, ["functional"] = true,
			["ok"] = true, ["okay"] = true, ["good"] = true, ["normal"] = true, ["online"] = true, ["enabled"] = true,
			["true"] = true, ["yes"] = true, ["y"] = true, ["t"] = true, ["1"] = true,
			["outofservice"] = false, ["oos"] = false, ["notinservice"] = false, ["outoforder"] = false, ["inactive"] = false, ["unavailable"] = false,
			["broken"] = false, ["damaged"] = false, ["defective"] = false, ["inoperable"] = false, ["inoperative"] = false, ["down"] = false,
			["repair"] = false, ["needsrepair"] = false, ["repairneeded"] = false, ["underrepair"] = false, ["offline"] = false, ["disabled"] = false,
			["removed"] = false, ["abandoned"] = false, ["retired"] = false, ["decommissioned"] = false,
			["false"] = false, ["no"] = false, ["n"] = false, ["f"] = false, ["0"] = false
		};

		/// <summary>Words that, ahead of "out of service", say the opposite ("Not out of service", "No longer out of order").</summary>
		private static readonly HashSet<string> Negations = new HashSet<string>(StringComparer.Ordinal) { "not", "no", "never", "isnt", "arent", "wasnt" };

		/// <summary>
		/// A service state from another system's status column: blank gives none (the hydrant keeps its state; a new one is in service).
		/// "Temporarily out of service" and the like count as out of service, but not a negated one ("No longer out of service").
		/// Anything else (passed, due...) gives none and is reported.
		/// </summary>
		private static bool? ReadForeignStatus(string text, string column, Batch batch)
		{
			if (string.IsNullOrWhiteSpace(text) || NoValue.Contains(text.Trim())) return null;
			var word = new string(text.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
			if (StatusWords.TryGetValue(word, out var inService)) return inService;
			if ((word.Contains("outofservice") || word.Contains("outoforder")) && !NegatesOutOfService(text)) return false;
			batch.AddUnmapped(column, text.Trim());
			return null;
		}

		/// <summary>
		/// True when a negation word comes before the "out of service" / "out of order" phrase in the same clause ("Not out of
		/// service", "No longer out of order"). One in an earlier clause ("Not working - out of service") does not count. One pass
		/// over the text: a status cell has no length limit of its own.
		/// </summary>
		private static bool NegatesOutOfService(string text)
		{
			// Words and the clause each is in. Apostrophes go (isn't = isnt); a hyphen inside a word ("out-of-service") only
			// separates words, while other punctuation, or a dash with a space beside it, starts a new clause.
			var words = new List<(string Word, int Clause)>();
			var current = new StringBuilder();
			var clause = 0;
			var lower = text.ToLowerInvariant();
			for (var i = 0; i < lower.Length; i++)
			{
				var c = lower[i];
				if (char.IsLetterOrDigit(c)) { current.Append(c); continue; }
				if (c == '\'' || c == '’') continue;
				if (current.Length > 0) { words.Add((current.ToString(), clause)); current.Clear(); }
				if (char.IsWhiteSpace(c)) continue;
				var hyphenInWord = c == '-' && i > 0 && i + 1 < lower.Length && char.IsLetterOrDigit(lower[i - 1]) && char.IsLetterOrDigit(lower[i + 1]);
				if (!hyphenInWord) clause++;
			}
			if (current.Length > 0) words.Add((current.ToString(), clause));

			// The words joined once, with where each starts: the phrase may span words ("out of service") or be one
			// ("OutOfService"), and must begin at a word.
			var joined = new StringBuilder();
			var wordAt = new Dictionary<int, int>();
			for (var w = 0; w < words.Count; w++) { wordAt[joined.Length] = w; joined.Append(words[w].Word); }
			var all = joined.ToString();

			var phraseWord = -1;
			foreach (var phrase in new[] { "outofservice", "outoforder" })
			{
				for (var at = all.IndexOf(phrase, StringComparison.Ordinal); at >= 0; at = all.IndexOf(phrase, at + 1, StringComparison.Ordinal))
				{
					if (!wordAt.TryGetValue(at, out var index)) continue;
					if (phraseWord < 0 || index < phraseWord) phraseWord = index;
					break;
				}
			}

			for (var w = phraseWord - 1; w >= 0 && words[w].Clause == words[phraseWord].Clause; w--)
				if (Negations.Contains(words[w].Word)) return true;

			return false;
		}

		private static void Add(Input input, HydrantImportRow row, Batch batch, HashSet<string> numbers, RmsHydrantFlowClass flowClass = RmsHydrantFlowClass.Unknown,
			bool? inService = null, string outOfServiceReason = null)
		{
			row.HydrantNumber = input.Number?.Trim();
			var errors = new List<string>();
			if (string.IsNullOrWhiteSpace(row.HydrantNumber) || row.HydrantNumber.Length > 64) errors.Add("number: enter a unique hydrant number of 1–64 characters.");
			else if (!numbers.Add(row.HydrantNumber)) errors.Add("number: this hydrant appears more than once in the file. Keep one row per hydrant number.");
			if (input.Latitude < -90 || input.Latitude > 90) errors.Add("latitude: use a number between -90 and 90.");
			if (input.Longitude < -180 || input.Longitude > 180) errors.Add("longitude: use a number between -180 and 180.");
			if (input.MainSize.HasValue && (input.MainSize <= 0 || input.MainSize > 9999.99m || decimal.Round(input.MainSize.Value, 2) != input.MainSize)) errors.Add("main_size: enter inches greater than 0 and at most 9999.99, using at most two decimal places.");
			if (input.FlowGpm < 0) errors.Add("flow_gpm: enter 0 or a positive whole number.");
			if (input.Address?.Length > 500) errors.Add("address: shorten to 500 characters or fewer.");
			if (input.Owner?.Length > 200) errors.Add("owner: shorten to 200 characters or fewer.");
			if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
			batch.Rows.Add((row, new RmsHydrant { HydrantNumber = row.HydrantNumber, Latitude = input.Latitude, Longitude = input.Longitude,
				Type = (int)(input.Type ?? RmsHydrantType.DryBarrel), AddressText = input.Address, MainSizeInches = input.MainSize, FlowGpm = input.FlowGpm, FlowClass = (int)flowClass,
				OwnerName = input.Owner, OwnerKind = (int)(string.IsNullOrWhiteSpace(input.Owner) ? RmsHydrantOwnerKind.Municipal : RmsHydrantOwnerKind.Other),
				InService = inService ?? true, OutOfServiceReason = outOfServiceReason }, inService));
		}

		private static void Reject(HydrantImportRow row, string error, Batch batch) { row.Error = error; batch.Result.Rejected.Add(row); }
		private static ArgumentException TooManyRows() => new ArgumentException("The import exceeds 20,000 hydrants. Split it into smaller files. No hydrants were saved.");
	}
}
