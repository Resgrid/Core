using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Onboarding;
using Resgrid.Providers.Import.Readers;
using Resgrid.Providers.Import.Templates;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>Readers and L0 inspection (wizard plan sections 8.3, 14.1 and 24.1 "Reader tests" and "Upload security").</summary>
	[TestFixture]
	public class ImportFileReaderTests
	{
		private readonly ImportFileReader _reader = new ImportFileReader();

		private static MemoryStream Text(string content, bool bom = false, Encoding encoding = null)
		{
			var bytes = (encoding ?? new UTF8Encoding(false)).GetBytes(content);
			return new MemoryStream(bom ? Encoding.UTF8.GetPreamble().Concat(bytes).ToArray() : bytes);
		}

		private List<(int Row, string[] Cells)> Read(Stream stream, ImportFileFormat format, int sheet = 0, ImportReadOptions options = null)
		{
			var rows = new List<(int, string[])>();
			var result = _reader.ReadSheet(stream, format, sheet, options ?? new ImportReadOptions(), (row, cells) => rows.Add((row, cells)));
			result.ErrorCode.Should().BeNull();
			return rows;
		}

		[Test]
		public void Csv_with_bom_quotes_and_multiline_fields_reads_every_row()
		{
			var csv = "Name,Notes\r\n\"Rivera, Alex\",\"line one\nline two\"\r\nSam,\"say \"\"hi\"\"\"\r\n";
			using var stream = Text(csv, bom: true);

			var inspection = _reader.Inspect(stream, "people.csv");
			inspection.Format.Should().Be(ImportFileFormat.Csv);
			inspection.Delimiter.Should().Be(",");

			var rows = Read(stream, ImportFileFormat.Csv);
			rows.Should().HaveCount(3);
			rows[1].Cells.Should().Equal("Rivera, Alex", "line one\nline two");
			rows[2].Cells.Should().Equal("Sam", "say \"hi\"");
		}

		[TestCase("a;b;c\n1;2;3\n4;5;6", ";")]
		[TestCase("a\tb\tc\n1\t2\t3", "\t")]
		[TestCase("a|b|c\n1|2|3", "|")]
		public void Delimiters_are_sniffed(string content, string delimiter)
		{
			using var stream = Text(content);
			_reader.Inspect(stream, "data.txt").Delimiter.Should().Be(delimiter);
		}

		[Test]
		public void Invalid_utf8_needs_the_administrators_confirmation_for_windows_1252()
		{
			var bytes = Encoding.UTF8.GetBytes("Name\nJos").Concat(new byte[] { 0xE9 }).Concat(Encoding.UTF8.GetBytes("\n")).ToArray();
			using var stream = new MemoryStream(bytes);

			_reader.Inspect(stream, "people.csv").RejectionCode.Should().Be("IMP-FILE-ENCODING");
			_reader.ReadSheet(stream, ImportFileFormat.Csv, 0, new ImportReadOptions(), (_, __) => { }).ErrorCode.Should().Be("IMP-FILE-ENCODING");

			var rows = Read(stream, ImportFileFormat.Csv, options: new ImportReadOptions { AllowWindows1252 = true });
			rows[1].Cells[0].Should().Be("José");
		}

		[Test]
		public void Row_and_field_caps_are_enforced()
		{
			using var stream = Text("h\n" + string.Join("\n", Enumerable.Range(1, 10)));
			var result = _reader.ReadSheet(stream, ImportFileFormat.Csv, 0, new ImportReadOptions { MaxRows = 5 }, (_, __) => { });
			result.Truncated.Should().BeTrue();

			using var wide = Text("h\n" + new string('x', 200));
			_reader.ReadSheet(wide, ImportFileFormat.Csv, 0, new ImportReadOptions { MaxFieldLength = 100 }, (_, __) => { }).ErrorCode.Should().Be("IMP-FILE-FIELD-TOO-LONG");
		}

		[Test]
		public void Generated_workbooks_read_back_with_inline_strings_hidden_lists_and_example_markers()
		{
			var writer = new ImportTemplateWriter();
			var bytes = writer.WriteWorkbook(new ImportTemplateWorkbook
			{
				Title = "Starter",
				Instructions = { "Fill in each sheet." },
				Sheets =
				{
					new ImportTemplateSheet
					{
						Name = "People",
						Columns = { new ImportTemplateColumn { Header = "First name [first_name]" }, new ImportTemplateColumn { Header = "Group [group]", ListName = "Groups" } },
						ExampleRows = { new[] { "Alex", "Station 1" } }
					}
				},
				Lists = { ["Groups"] = new List<string> { "Station 1", "Station 2" } }
			});

			using var stream = new MemoryStream(bytes);
			_reader.Inspect(stream, "starter.xlsx").Format.Should().Be(ImportFileFormat.Xlsx);

			var sheets = _reader.ListSheets(stream, ImportFileFormat.Xlsx);
			sheets.Select(s => s.Name).Should().Equal("Instructions", "People", "Lists");
			sheets.Single(s => s.Name == "Lists").Hidden.Should().BeTrue();

			var rows = Read(stream, ImportFileFormat.Xlsx, 1);
			rows[0].Cells.Should().Equal("First name [first_name]", "Group [group]", ImportTemplateMarkers.ExampleColumn);
			rows[1].Cells.Should().Equal("Alex", "Station 1", "x");
		}

		[Test]
		public void Excel_serial_dates_honour_both_date_systems()
		{
			ImportFileReader.FromSerial(45383, false).Should().Be(new DateTime(2024, 4, 1));
			ImportFileReader.FromSerial(43921, true).Should().Be(new DateTime(2024, 4, 1));
			ImportFileReader.FromSerial(59, false).Should().Be(new DateTime(1900, 2, 28));
			ImportFileReader.FromSerial(61, false).Should().Be(new DateTime(1900, 3, 1), "serial 60 is Excel's phantom 1900-02-29");
		}

		[TestCase("yyyy-mm-dd", true)]
		[TestCase("[$-409]d-mmm-yy;@", true)]
		[TestCase("h:mm", false)]
		[TestCase("\"day\" 0", false)]
		[TestCase("0.00", false)]
		public void Custom_number_formats_are_recognized_as_dates(string code, bool isDate)
		{
			ImportFileReader.LooksLikeDateFormat(code).Should().Be(isDate);
		}

		[Test]
		public void Macro_enabled_binary_and_encrypted_workbooks_are_refused()
		{
			using var macro = Zip(("[Content_Types].xml", "x"), ("xl/workbook.xml", "x"), ("xl/vbaProject.bin", "x"));
			_reader.Inspect(macro, "book.xlsx").RejectionCode.Should().Be("IMP-FILE-MACRO");

			using var plain = Text("a,b");
			_reader.Inspect(plain, "book.xlsm").RejectionCode.Should().Be("IMP-FILE-MACRO");
			_reader.Inspect(plain, "book.xls").RejectionCode.Should().Be("IMP-FILE-SIGNATURE");

			using var ole = new MemoryStream(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0 });
			_reader.Inspect(ole, "book.xlsx").RejectionCode.Should().Be("IMP-FILE-ENCRYPTED");

			using var binary = new MemoryStream(new byte[] { 0x41, 0x00, 0x42 });
			_reader.Inspect(binary, "people.csv").RejectionCode.Should().Be("IMP-FILE-SIGNATURE");
		}

		[Test]
		public void Zip_guards_refuse_traversal_nesting_and_bombs()
		{
			using var traversal = Zip(("../evil.csv", "a"));
			_reader.Inspect(traversal, "package.zip").RejectionCode.Should().Be("IMP-ZIP-PATH");

			using var nested = Zip(("inner.zip", "a"));
			_reader.Inspect(nested, "package.zip").RejectionCode.Should().Be("IMP-ZIP-NESTED");

			using var bomb = Zip(("big.csv", new string('a', 5_000_000)));
			_reader.Inspect(bomb, "package.zip").RejectionCode.Should().Be("IMP-ZIP-RATIO");
		}

		[Test]
		public void Json_arrays_wrapped_arrays_jsonl_and_geojson_become_rows()
		{
			using var array = Text("[{\"name\":\"A\",\"address\":{\"city\":\"X\"}},{\"name\":\"B\",\"tags\":[\"t1\",\"t2\"]}]");
			var rows = Read(array, ImportFileFormat.Json);
			rows[0].Cells.Should().Equal("name", "address.city", "tags");
			rows[1].Cells.Should().Equal("A", "X", "");
			rows[2].Cells.Should().Equal("B", "", "t1; t2");

			using var wrapped = Text("{\"data\":[{\"id\":1}]}");
			Read(wrapped, ImportFileFormat.Json)[1].Cells.Should().Equal("1");

			using var lines = Text("{\"id\":1}\n{\"id\":2}\n");
			_reader.Inspect(lines, "x.jsonl").Format.Should().Be(ImportFileFormat.JsonLines);
			Read(lines, ImportFileFormat.JsonLines).Should().HaveCount(3);

			using var geo = Text("{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{\"name\":\"H1\"},\"geometry\":{\"type\":\"Point\",\"coordinates\":[-89.6,39.8]}}]}");
			_reader.Inspect(geo, "hydrants.geojson").Format.Should().Be(ImportFileFormat.GeoJson);
			var geoRows = Read(geo, ImportFileFormat.GeoJson);
			geoRows[0].Cells.Should().Equal("name", "longitude", "latitude");
			geoRows[1].Cells.Should().Equal("H1", "-89.6", "39.8");
		}

		[Test]
		public void Invalid_json_reports_a_line_never_the_content()
		{
			using var lines = Text("{\"id\":1}\n{\"secret\":\"do-not-echo\"\n");
			var result = _reader.ReadSheet(lines, ImportFileFormat.JsonLines, 0, new ImportReadOptions(), (_, __) => { });
			result.ErrorCode.Should().Be("IMP-PARSE-JSON");
			result.ErrorRow.Should().Be(2);
		}

		private static MemoryStream Zip(params (string Name, string Content)[] entries)
		{
			var stream = new MemoryStream();
			using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
			{
				foreach (var (name, content) in entries)
				{
					var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
					using var writer = new StreamWriter(entry.Open());
					writer.Write(content);
				}
			}
			stream.Position = 0;
			return stream;
		}
	}
}
