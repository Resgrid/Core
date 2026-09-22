using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
			public List<(HydrantImportRow Row, RmsHydrant Hydrant)> Rows { get; } = new List<(HydrantImportRow, RmsHydrant)>();
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
			var header = new[] { "number", "latitude", "longitude", "type", "address", "main_size", "flow_gpm", "owner" };
			var first = true;
			try
			{
				while (!parser.EndOfData)
				{
					var line = (int)parser.LineNumber;
					var cells = parser.ReadFields();
					if (first)
					{
						first = false;
						var candidate = cells.Select(CanonicalHeader).ToArray();
						if (candidate.Contains("number") || candidate.Contains("latitude") || candidate.Contains("longitude"))
						{
							if (candidate.Distinct().Count() != candidate.Length) throw new ArgumentException("The CSV contains duplicate column names. Keep one column for each field.");
							if (!new[] { "number", "latitude", "longitude" }.All(candidate.Contains)) throw new ArgumentException("The CSV header must include number, latitude and longitude.");
							if (candidate.Except(header).Any()) throw new ArgumentException("Unrecognized CSV column: " + string.Join(", ", candidate.Except(header)) + ". Use the column names in the example.");
							header = candidate;
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
						if (!string.IsNullOrWhiteSpace(flow))
						{
							if (!int.TryParse(flow, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) throw new ArgumentException("flow_gpm: enter a whole number between 0 and 2147483647, without decimals.");
							flowGpm = value;
						}
						Add(new Input { Number = row.HydrantNumber, Latitude = Number("latitude", true).Value, Longitude = Number("longitude", true).Value,
							Type = ParseType(Cell("type")), Address = Cell("address"), MainSize = Number("main_size"), FlowGpm = flowGpm, Owner = Cell("owner") }, row, batch, numbers);
					}
					catch (ArgumentException ex) { Reject(row, ex.Message, batch); }
				}
			}
			catch (MalformedLineException) { throw new ArgumentException($"CSV line {parser.ErrorLineNumber} has unmatched quotes. Enclose each quoted value in double quotes. No hydrants were saved."); }
		}

		private static string CanonicalHeader(string value)
		{
			var name = value.Trim().ToLowerInvariant().Replace(" ", "_");
			return name switch { "hydrant_number" or "id" => "number", "lat" => "latitude", "lon" or "lng" => "longitude", "main_size_inches" or "main" => "main_size", "flow" => "flow_gpm", "owner_name" => "owner", _ => name };
		}

		private static RmsHydrantType ParseType(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return RmsHydrantType.DryBarrel;
			var value = text.Trim().Replace(" ", "").Replace("-", "").Replace("_", "");
			value = value.ToLowerInvariant() switch { "dry" => "DryBarrel", "wet" => "WetBarrel", "tank" => "Cistern", "draft" or "pond" or "lake" or "river" => "DraftingSite", _ => value };
			if (Enum.TryParse<RmsHydrantType>(value, true, out var type) && Enum.IsDefined(type)) return type;
			throw new ArgumentException("type: use DryBarrel, WetBarrel, Standpipe, Cistern, DraftingSite or Other (or 1–6).");
		}

		private static void Add(Input input, HydrantImportRow row, Batch batch, HashSet<string> numbers)
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
				Type = (int)(input.Type ?? RmsHydrantType.DryBarrel), AddressText = input.Address, MainSizeInches = input.MainSize, FlowGpm = input.FlowGpm,
				OwnerName = input.Owner, OwnerKind = (int)(string.IsNullOrWhiteSpace(input.Owner) ? RmsHydrantOwnerKind.Municipal : RmsHydrantOwnerKind.Other) }));
		}

		private static void Reject(HydrantImportRow row, string error, Batch batch) { row.Error = error; batch.Result.Rejected.Add(row); }
		private static ArgumentException TooManyRows() => new ArgumentException("The import exceeds 20,000 hydrants. Split it into smaller files. No hydrants were saved.");
	}
}
