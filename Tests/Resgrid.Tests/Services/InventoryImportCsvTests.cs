using System;
using System.Globalization;
using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Inventories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>Reading the inventory import file: forgiving about layout, strict about values it could misread.</summary>
	[TestFixture]
	public sealed class InventoryImportCsvTests
	{
		private static readonly string RequestId = Guid.NewGuid().ToString("D");

		private static InventoryException Refused(Action action) => action.Should().Throw<InventoryException>().Which;

		[Test]
		public void Columns_match_by_alias_in_any_order_and_unknown_ones_are_reported()
		{
			var input = InventoryImportCsv.Parse("qty,Colour,Unit Cost,item name,SKU\r\n5,red,1.25,Bandage,B-1\r\n", RequestId);

			var row = input.Rows.Single();
			row.Item.Should().Be("Bandage"); row.Quantity.Should().Be(5); row.UnitCost.Should().Be(1.25m); row.Code.Should().Be("B-1");
			row.RowNumber.Should().Be(2, "the header is the spreadsheet's first row");
			input.IgnoredColumns.Should().Equal("Colour");
		}

		[Test]
		public void A_semicolon_file_reads_comma_decimals_and_local_dates()
		{
			var input = InventoryImportCsv.Parse("Artikel;Item;Quantity;UnitCost;Expires\r\nx;Kochsalzlösung;1.250;2,75;31.03.2027\r\n", RequestId, new CultureInfo("de-DE"));

			var row = input.Rows.Single();
			row.Quantity.Should().Be(1250m); row.UnitCost.Should().Be(2.75m);
			row.Expires.Should().Be(new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc)); row.Expires.Value.Kind.Should().Be(DateTimeKind.Utc);
		}

		[TestCase("1,234.5", '.', ',', 1234.5)]
		[TestCase("1.234,5", ',', '.', 1234.5)]
		[TestCase("1,5", '.', ',', 1.5)]
		[TestCase("1.5", ',', '.', 1.5)]
		[TestCase("$12.50", '.', ',', 12.5)]
		[TestCase("12,50 €", ',', '.', 12.5)]
		[TestCase("-3", '.', ',', -3)]
		[TestCase("1 234,5", ',', '.', 1234.5)]
		public void Numbers_follow_the_file_convention(string text, char decimalSeparator, char groupSeparator, decimal expected)
		{
			InventoryImportCsv.TryNumber(text, decimalSeparator, groupSeparator, out var value).Should().BeTrue();
			value.Should().Be(expected);
		}

		[TestCase("1,23,4")]
		[TestCase("12a4")]
		[TestCase("abc")]
		[TestCase("1.2.3")]
		public void Numbers_that_could_be_misread_are_refused(string text)
			=> InventoryImportCsv.TryNumber(text, '.', ',', out _).Should().BeFalse();

		[Test]
		public void A_bad_value_fails_only_its_row_and_names_the_column()
		{
			var input = InventoryImportCsv.Parse("Item,Quantity,RequiresLot,Tracking,Expires\r\nA,abc,,,\r\nB,1,maybe,,\r\nC,1,,Pallet,\r\nD,1,,,someday\r\nE,1,yes,serial,2027-01-01\r\n", RequestId);

			input.Rows.Select(r => (r.ParseError, r.ParseColumn)).Should().Equal(
				("ImportInvalidNumber", "Quantity"), ("ImportInvalidYesNo", "RequiresLot"), ("ImportInvalidTracking", "Tracking"), ("ImportInvalidDate", "Expires"), (null, null));
			input.Rows.Last().RequiresLot.Should().BeTrue(); input.Rows.Last().Tracking.Should().Be(InventoryTrackingMode.Serialized);
		}

		[Test]
		public void An_unquoted_comma_inside_a_value_is_caught_rather_than_shifting_columns()
		{
			var row = InventoryImportCsv.Parse("Item,Quantity\r\nA,1,5\r\n", RequestId).Rows.Single();
			row.ParseError.Should().Be("ImportRowColumns");
		}

		[Test]
		public void Blank_rows_are_skipped_and_the_export_formula_guard_is_undone()
		{
			var input = InventoryImportCsv.Parse("\"Item\",\"Quantity\",\"Note\"\r\n\r\n,,\r\n\"'=Gauze\",\"5\",\"'-checked\"\r\n", RequestId);

			var row = input.Rows.Single();
			row.RowNumber.Should().Be(4); row.Item.Should().Be("=Gauze"); row.Note.Should().Be("-checked");
		}

		[Test]
		public void Inch_marks_and_multi_line_cells_read_as_a_spreadsheet_shows_them()
		{
			var input = InventoryImportCsv.Parse("Item,Description,Quantity\r\nGauze 4\"x4\",plain,10\r\nSplint,\"Line one\r\nline \"\"two\"\"\",2\r\nTape,,3\r\n", RequestId);

			input.Rows.Select(r => (r.RowNumber, r.Item)).Should().Equal((2, "Gauze 4\"x4\""), (3, "Splint"), (4, "Tape"));
			input.Rows[1].Description.Should().Be("Line one\r\nline \"two\"");
			InventoryImportCsv.Parse("Item,Note\r\nA,\"never closed\r\n", RequestId).Rows.Single().ParseError.Should().Be("ImportRowUnreadable");
		}

		[Test]
		public void The_header_must_name_items_once()
		{
			Refused(() => InventoryImportCsv.Parse("Quantity\r\n5\r\n", RequestId)).Code.Should().Be("ImportItemColumnRequired");
			Refused(() => InventoryImportCsv.Parse("Name,Item\r\nA,B\r\n", RequestId)).Code.Should().Be("ImportDuplicateColumn");
			Refused(() => InventoryImportCsv.Parse("Item,Quantity\r\n", RequestId)).Code.Should().Be("ImportFileEmpty");
			Refused(() => InventoryImportCsv.Parse("", RequestId)).Code.Should().Be("ImportFileInvalid");
		}

		[Test]
		public void A_file_over_the_row_limit_is_refused()
		{
			var text = new StringBuilder("Item,Quantity\r\n");
			for (var i = 0; i <= InventoryImportLimits.MaxRows; i++) text.Append("Item ").Append(i).Append(",1\r\n");
			Refused(() => InventoryImportCsv.Parse(text.ToString(), RequestId)).Code.Should().Be("ImportTooManyRows");
		}

		[Test]
		public void Tab_separated_unicode_text_from_a_spreadsheet_is_read()
		{
			var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Item\tQuantity\r\nCafé filters\t3\r\n")).ToArray();

			var row = InventoryImportCsv.Parse(InventoryImportCsv.Decode(bytes), RequestId).Rows.Single();

			row.Item.Should().Be("Café filters"); row.Quantity.Should().Be(3);
		}

		[Test]
		public void Plain_csv_that_is_not_utf8_falls_back_to_latin1()
		{
			var bytes = new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xE9 };
			InventoryImportCsv.Decode(bytes).Should().Be("Café");
			InventoryImportCsv.Decode(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("Café")).ToArray()).Should().Be("Café");
		}

		[Test]
		public void The_template_is_the_header_in_the_documented_order()
		{
			var template = InventoryImportCsv.Template();
			template.Should().EndWith("\r\n");
			template.TrimEnd().Split(',').Select(c => c.Trim('"')).Should().Equal(InventoryImportCsv.Header);
			InventoryImportCsv.Header.Should().OnlyContain(column => InventoryImportCsv.ColumnFor(column) == column);
		}
	}
}
