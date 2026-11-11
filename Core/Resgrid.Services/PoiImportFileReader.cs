using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using Resgrid.Model;

namespace Resgrid.Services
{
	/// <summary>
	/// Reads POI rows from a spreadsheet export (CSV) or from a KML placemark's ExtendedData. Columns are recognized by
	/// name, whatever the tool that wrote them: a position (latitude/longitude) is used as-is, otherwise the address
	/// columns (street, city, state, postal code, country) are joined for geocoding.
	/// </summary>
	public static class PoiImportFileReader
	{
		public const int MaximumRows = 10000;

		private enum Field { Name, Latitude, Longitude, Street, FullAddress, City, State, PostalCode, Country, Note }

		// Compared after lower-casing and removing everything but letters and digits ("Street Address 1" -> "streetaddress1").
		// "Region" is deliberately absent: spreadsheets use it for sales or response regions as often as for a state.
		private static readonly Dictionary<string, Field> Headers = new Dictionary<string, Field>(StringComparer.Ordinal)
		{
			["name"] = Field.Name, ["title"] = Field.Name, ["label"] = Field.Name, ["poiname"] = Field.Name, ["sitename"] = Field.Name,
			["locationname"] = Field.Name, ["placename"] = Field.Name, ["facilityname"] = Field.Name,
			["latitude"] = Field.Latitude, ["lat"] = Field.Latitude, ["y"] = Field.Latitude, ["pointy"] = Field.Latitude, ["ycoord"] = Field.Latitude,
			["longitude"] = Field.Longitude, ["lon"] = Field.Longitude, ["lng"] = Field.Longitude, ["long"] = Field.Longitude, ["x"] = Field.Longitude,
			["pointx"] = Field.Longitude, ["xcoord"] = Field.Longitude,
			["address"] = Field.Street, ["address1"] = Field.Street, ["addressline1"] = Field.Street, ["street"] = Field.Street,
			["street1"] = Field.Street, ["streetaddress"] = Field.Street, ["streetaddress1"] = Field.Street,
			["fulladdress"] = Field.FullAddress, ["formattedaddress"] = Field.FullAddress, ["completeaddress"] = Field.FullAddress,
			["city"] = Field.City, ["town"] = Field.City, ["municipality"] = Field.City, ["locality"] = Field.City,
			["state"] = Field.State, ["province"] = Field.State, ["stateprovince"] = Field.State, ["stateorprovince"] = Field.State,
			["postalcode"] = Field.PostalCode, ["postcode"] = Field.PostalCode, ["postal"] = Field.PostalCode, ["zip"] = Field.PostalCode,
			["zipcode"] = Field.PostalCode,
			["country"] = Field.Country,
			["note"] = Field.Note, ["notes"] = Field.Note, ["comment"] = Field.Note, ["comments"] = Field.Note, ["remarks"] = Field.Note,
			["description"] = Field.Note
		};

		// Spreadsheet tools that treat a ZIP code as a number write "60611.0".
		private static readonly Regex NumericPostalCode = new Regex(@"^(\d+)\.0+$", RegexOptions.CultureInvariant);

		private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

		public sealed class CsvResult
		{
			public List<PoiImportRow> Rows { get; } = new List<PoiImportRow>();

			/// <summary>Data rows with neither a position nor an address.</summary>
			public int SkippedCount { get; set; }

			public string Error { get; set; }
		}

		/// <summary>The POI in a set of named values (a CSV row, or a KML placemark's ExtendedData); unrecognized names are ignored.</summary>
		public static PoiImportRow FromFields(IEnumerable<KeyValuePair<string, string>> fields, char decimalSeparatorHint = '.')
		{
			var values = new Dictionary<Field, string>();

			foreach (var pair in fields ?? Enumerable.Empty<KeyValuePair<string, string>>())
			{
				if (Headers.TryGetValue(Canonical(pair.Key), out var field) && !values.ContainsKey(field) && !string.IsNullOrWhiteSpace(pair.Value))
					values[field] = pair.Value.Trim();
			}

			string Value(Field field) => values.TryGetValue(field, out var value) ? value : null;

			var row = new PoiImportRow { Name = Value(Field.Name), Note = Value(Field.Note), Address = AddressOf(Value) };

			var latitude = Coordinate(Value(Field.Latitude), decimalSeparatorHint);
			var longitude = Coordinate(Value(Field.Longitude), decimalSeparatorHint);

			if (latitude.HasValue && longitude.HasValue && IsValidPosition(latitude.Value, longitude.Value))
			{
				row.Latitude = latitude;
				row.Longitude = longitude;
			}

			return row;
		}

		/// <summary>Reads a CSV whose first row names the columns. Comma, semicolon and tab delimiters are detected from that row.</summary>
		public static CsvResult ReadCsv(Stream input)
		{
			var result = new CsvResult();
			string text;

			using (var buffer = new MemoryStream())
			{
				input.CopyTo(buffer);
				text = Decode(buffer.ToArray());
			}

			var firstLine = string.Empty;
			using (var lines = new StringReader(text))
			{
				string line;
				while ((line = lines.ReadLine()) != null && string.IsNullOrWhiteSpace(line)) { }
				firstLine = line ?? string.Empty;
			}

			var delimiter = new[] { ',', ';', '\t' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();

			using (var parser = new TextFieldParser(new StringReader(text)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true })
			{
				parser.SetDelimiters(delimiter.ToString());
				string[] header = null;

				try
				{
					while (!parser.EndOfData)
					{
						var cells = parser.ReadFields();
						if (cells == null || cells.All(string.IsNullOrWhiteSpace))
							continue;

						if (header == null)
						{
							header = cells;
							var recognized = header.Select(h => Headers.TryGetValue(Canonical(h), out var f) ? f : (Field?)null).Where(f => f.HasValue).Select(f => f.Value).ToList();

							var hasPosition = recognized.Contains(Field.Latitude) && recognized.Contains(Field.Longitude);
							var hasAddress = recognized.Contains(Field.Street) || recognized.Contains(Field.FullAddress);

							if (!hasPosition && !hasAddress)
							{
								result.Error = "The first row of the CSV must name the columns, with latitude and longitude columns or an address column " +
								               "(city, state and postal code columns are added to the address when present). Columns found: " +
								               string.Join(", ", header.Where(h => !string.IsNullOrWhiteSpace(h)).Take(20)) + ".";
								return result;
							}

							continue;
						}

						if (result.Rows.Count + result.SkippedCount >= MaximumRows)
						{
							result.Error = $"The CSV has more than {MaximumRows} rows. Split it into smaller files and import each one.";
							result.Rows.Clear();
							return result;
						}

						var row = FromFields(header.Select((h, i) => new KeyValuePair<string, string>(h, i < cells.Length ? cells[i] : null)), delimiter == ',' ? '.' : ',');

						if (string.IsNullOrWhiteSpace(row.Name))
							row.Name = row.Address;

						if (row.HasPosition || !string.IsNullOrWhiteSpace(row.Address))
							result.Rows.Add(row);
						else
							result.SkippedCount++;
					}
				}
				catch (MalformedLineException ex)
				{
					result.Error = $"Line {ex.LineNumber} of the CSV could not be read: check for an unmatched double quote.";
					result.Rows.Clear();
					return result;
				}

				if (header == null)
					result.Error = "The CSV is empty.";
			}

			return result;
		}

		private static string AddressOf(Func<Field, string> value)
		{
			var full = value(Field.FullAddress);
			if (!string.IsNullOrWhiteSpace(full))
				return full;

			var street = value(Field.Street);
			if (string.IsNullOrWhiteSpace(street))
				return null; // A city or postal code alone would put the POI at the town's center.

			var postalCode = value(Field.PostalCode);
			if (postalCode != null)
			{
				var numeric = NumericPostalCode.Match(postalCode);
				if (numeric.Success)
					postalCode = numeric.Groups[1].Value;
			}

			var parts = new[] { street, value(Field.City), value(Field.State), postalCode, value(Field.Country) };
			return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
		}

		private static double? Coordinate(string value, char decimalSeparatorHint)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			// A semicolon-delimited export from a comma-decimal locale writes 41,59.
			if (decimalSeparatorHint == ',' && value.IndexOf('.') < 0)
				value = value.Replace(',', '.');

			return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : (double?)null;
		}

		/// <summary>Out of range is usually latitude and longitude swapped; 0,0 is a converter's placeholder for "not geocoded".</summary>
		public static bool IsValidPosition(double latitude, double longitude) =>
			!double.IsNaN(latitude) && !double.IsNaN(longitude) &&
			latitude >= -90 && latitude <= 90 && longitude >= -180 && longitude <= 180 &&
			!(latitude == 0 && longitude == 0);

		private static string Canonical(string header) =>
			new string((header ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

		/// <summary>UTF-8 (with or without a byte order mark), UTF-16 with a byte order mark, else the Windows/Latin-1 bytes Excel writes.</summary>
		private static string Decode(byte[] content)
		{
			if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
				return Encoding.UTF8.GetString(content, 3, content.Length - 3);
			if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
				return Encoding.Unicode.GetString(content, 2, content.Length - 2);
			if (content.Length >= 2 && content[0] == 0xFE && content[1] == 0xFF)
				return Encoding.BigEndianUnicode.GetString(content, 2, content.Length - 2);

			try
			{
				return StrictUtf8.GetString(content);
			}
			catch (DecoderFallbackException)
			{
				return Encoding.Latin1.GetString(content);
			}
		}
	}
}
