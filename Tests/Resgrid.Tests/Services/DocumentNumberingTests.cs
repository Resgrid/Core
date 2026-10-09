using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Department document numbers (setting 117 + setting 72's DocumentPatterns + DocumentNumberSequences): built-in numbers until
	/// a department sets a pattern, fiscal numbering years per settings group, custom sequences seeded from numbers already
	/// issued, raised next numbers that only rise, and the patterns each kind's column can hold.
	/// </summary>
	[TestFixture]
	public class DocumentNumberingTests
	{
		private const int Dept = 11;

		private DocumentNumberingConfig _documents;
		private RecordsNumberingConfig _records;
		private Department _department;
		private Dictionary<string, List<string>> _issued;
		private FakeDocumentNumberSequences _sequences;
		private DocumentNumberingService _service;

		[SetUp]
		public void SetUp()
		{
			_documents = new DocumentNumberingConfig();
			_records = new RecordsNumberingConfig();
			_department = new Department { DepartmentId = Dept };
			_issued = new Dictionary<string, List<string>>();
			_sequences = new FakeDocumentNumberSequences { Issued = kind => _issued.TryGetValue(kind, out var list) ? list : Enumerable.Empty<string>() };

			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDocumentNumberingConfigAsync(Dept, It.IsAny<bool>()))
				.ReturnsAsync(() => ObjectSerialization.Deserialize<DocumentNumberingConfig>(ObjectSerialization.Serialize(_documents)));
			settings.Setup(s => s.SetDocumentNumberingConfigAsync(Dept, It.IsAny<DocumentNumberingConfig>(), It.IsAny<CancellationToken>()))
				.Callback((int d, DocumentNumberingConfig c, CancellationToken t) => _documents = c).ReturnsAsync(new DepartmentSetting());
			settings.Setup(s => s.GetRecordsNumberingConfigAsync(Dept, It.IsAny<bool>()))
				.ReturnsAsync(() => ObjectSerialization.Deserialize<RecordsNumberingConfig>(ObjectSerialization.Serialize(_records)));
			settings.Setup(s => s.SetRecordsNumberingConfigAsync(Dept, It.IsAny<RecordsNumberingConfig>(), It.IsAny<CancellationToken>()))
				.Callback((int d, RecordsNumberingConfig c, CancellationToken t) => _records = c).ReturnsAsync(new DepartmentSetting());
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => _department);

			_service = new DocumentNumberingService(settings.Object, departments.Object, _sequences);
		}

		private void Pattern(string kind, string pattern, int width) =>
			(DocumentNumberKinds.Find(kind).IsRecords ? _records.DocumentPatterns : _documents.Patterns).Add(new DocumentNumberPattern { Kind = kind, Pattern = pattern, SequenceWidth = width });

		private void Issued(string kind, params string[] numbers)
		{
			if (!_issued.TryGetValue(kind, out var list))
				_issued[kind] = list = new List<string>();
			list.AddRange(numbers);
		}

		#region Formats

		[TestCase("1042", "#1042")]
		[TestCase("00042", "#00042")]
		[TestCase("INV-2026-0001", "INV-2026-0001")]
		[TestCase("", "")]
		[TestCase(null, null)]
		public void A_number_of_digits_alone_reads_with_a_hash(string number, string expected)
		{
			DocumentNumbering.Display(number).Should().Be(expected);
		}

		[Test]
		public void A_pattern_must_fit_the_column_of_the_kind_it_numbers()
		{
			// 24 characters of text plus an 8-digit sequence is 32: the prevention columns' limit.
			DocumentNumbering.IsValid(DocumentNumberKinds.Find(DocumentNumberKinds.Inspection), "FIRE-INSPECTION-{YYYY}-{MM}-{SEQ}").Should().BeTrue();
			DocumentNumbering.IsValid(DocumentNumberKinds.Find(DocumentNumberKinds.Inspection), "FIRE-INSPECTIONS-{YYYY}-{MM}-{SEQ}").Should().BeFalse();
			DocumentNumbering.IsValid(DocumentNumberKinds.Find(DocumentNumberKinds.Invoice), "FIRE-INSPECTIONS-{YYYY}-{MM}-{SEQ}").Should().BeTrue("invoice numbers have 50 characters");
			DocumentNumbering.IsValid(DocumentNumberKinds.Find(DocumentNumberKinds.Invoice), "INV {SEQ}").Should().BeFalse();
			DocumentNumbering.MaxRenderedLength("WO-{YY}{MM}-{SEQ}").Should().Be("WO-0000-".Length + 8);
		}

		[Test]
		public void A_saved_pattern_that_is_the_built_in_one_keeps_the_built_in_numbers()
		{
			// Tokens match in any case; fixed text is written as typed, so "wo-" is a pattern of its own.
			var patterns = new List<DocumentNumberPattern> { new DocumentNumberPattern { Kind = DocumentNumberKinds.WorkOrder, Pattern = "WO-{yyyy}-{seq}", SequenceWidth = 6 } };
			DocumentNumbering.EffectivePattern(patterns, DocumentNumberKinds.WorkOrder).Should().BeNull();

			patterns[0].SequenceWidth = 4;
			DocumentNumbering.EffectivePattern(patterns, DocumentNumberKinds.WorkOrder).Pattern.Should().Be("WO-{YYYY}-{SEQ}");
			patterns[0].SequenceWidth = 6;
			patterns[0].Pattern = "wo-{YYYY}-{SEQ}";
			DocumentNumbering.EffectivePattern(patterns, DocumentNumberKinds.WorkOrder).Pattern.Should().Be("wo-{YYYY}-{SEQ}");
		}

		[Test]
		public void The_config_round_trips_through_protobuf()
		{
			var config = new DocumentNumberingConfig { YearStartMonth = 7, YearStartDay = 1, YearLabel = (int)NumberingYearLabel.StartYear };
			config.Patterns.Add(new DocumentNumberPattern { Kind = DocumentNumberKinds.Invoice, Pattern = "INV-{YYYY}-{SEQ}", SequenceWidth = 4 });

			var copy = ObjectSerialization.Deserialize<DocumentNumberingConfig>(ObjectSerialization.Serialize(config));

			copy.YearStart().Month.Should().Be(7);
			copy.YearStart().Label.Should().Be(NumberingYearLabel.StartYear);
			copy.Patterns.Should().ContainSingle().Which.Pattern.Should().Be("INV-{YYYY}-{SEQ}");
			ObjectSerialization.Deserialize<DocumentNumberingConfig>(ObjectSerialization.Serialize(new DocumentNumberingConfig())).YearStart().IsCalendarYear.Should().BeTrue();
		}

		#endregion

		#region Allocation

		[Test]
		public async Task Every_kind_keeps_its_built_in_numbers_until_the_department_sets_a_pattern()
		{
			foreach (var kind in DocumentNumberKinds.All)
				(await _service.TakeCustomNumberAsync(Dept, kind.Key, new DateTime(2026, 11, 2))).Should().BeNull(kind.Key);
			_sequences.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_custom_invoice_pattern_issues_its_own_sequence_and_restarts_each_numbering_year()
		{
			_documents.YearStartMonth = 11;
			_documents.YearStartDay = 1;
			Pattern(DocumentNumberKinds.Invoice, "INV-{YYYY}-{SEQ}", 4);

			(await _service.TakeCustomNumberAsync(Dept, DocumentNumberKinds.Invoice, new DateTime(2026, 10, 30))).Should().Be("INV-2026-0001");
			(await _service.TakeCustomNumberAsync(Dept, DocumentNumberKinds.Invoice, new DateTime(2026, 10, 31))).Should().Be("INV-2026-0002");
			(await _service.TakeCustomNumberAsync(Dept, DocumentNumberKinds.Invoice, new DateTime(2026, 11, 1, 0, 30, 0))).Should().Be("INV-2027-0001", "a November fiscal year named by the year it ends in");
			(await _service.TakeCustomNumberAsync(Dept, DocumentNumberKinds.Invoice, new DateTime(2027, 1, 4))).Should().Be("INV-2027-0002", "January 1 does not restart a fiscal year");
		}

		[Test]
		public async Task A_pattern_that_reads_like_numbers_already_issued_carries_on_after_them()
		{
			Issued(DocumentNumberKinds.Invoice, "1040", "1041", "1042");
			Pattern(DocumentNumberKinds.Invoice, "{SEQ}", 5);

			(await _service.TakeCustomNumberAsync(Dept, DocumentNumberKinds.Invoice, DateTime.UtcNow)).Should().Be("01043", "#1-#1042 already exist, so the padded pattern must not issue 00001");
			(await _service.TakeCustomNumberAsync(Dept, DocumentNumberKinds.Invoice, DateTime.UtcNow)).Should().Be("01044");
			_sequences.SeedScans.Should().Be(1, "only the first number of a scope reads the numbers already issued");
		}

		[Test]
		public async Task A_work_order_pattern_continues_the_built_in_numbers_of_its_year()
		{
			Issued(DocumentNumberKinds.WorkOrder, "WO-2026-000019", "WO-2025-000400");
			Pattern(DocumentNumberKinds.WorkOrder, "WO-{YYYY}-{SEQ}", 4);

			(await _service.TakeCustomNumberAsync(Dept, DocumentNumberKinds.WorkOrder, new DateTime(2026, 6, 1))).Should().Be("WO-2026-0020");
		}

		[Test]
		public async Task Documents_and_records_number_by_their_own_year_start_in_department_time()
		{
			_department.TimeZone = "Central Standard Time";
			_documents.YearStartMonth = 7;
			_documents.YearStartDay = 1;
			_records.YearStartMonth = 11;
			_records.YearStartDay = 1;

			var lateOctober = new DateTime(2026, 11, 1, 4, 30, 0, DateTimeKind.Utc); // 23:30 on 31 October in Chicago
			(await _service.GetNumberingYearAsync(Dept, DocumentNumberKinds.WorkOrder, lateOctober)).Should().Be(2027, "the July fiscal year 2027 began on 1 July 2026");
			(await _service.GetNumberingYearAsync(Dept, DocumentNumberKinds.Inspection, lateOctober)).Should().Be(2026, "Records' November year has not started yet in Chicago");
			(await _service.GetNumberingYearAsync(Dept, DocumentNumberKinds.Inspection, lateOctober.AddHours(2))).Should().Be(2027);

			_documents = new DocumentNumberingConfig();
			(await _service.GetNumberingYearAsync(Dept, DocumentNumberKinds.Invoice, new DateTime(2027, 1, 1, 3, 0, 0, DateTimeKind.Utc))).Should().Be(2026,
				"9 PM on 31 December in Chicago is still 2026, though it is 2027 in UTC");
		}

		#endregion

		#region Saving

		[Test]
		public async Task Saving_stores_the_year_start_and_valid_patterns_and_refuses_the_rest()
		{
			var result = await _service.SaveAsync(Dept, "admin", new DocumentNumberingUpdate
			{
				YearStartMonth = 11, YearStartDay = 1, YearLabel = (int)NumberingYearLabel.EndYear,
				Patterns =
				{
					new DocumentNumberPatternUpdate { Kind = DocumentNumberKinds.WorkOrder, Pattern = " mnt-{yy}-{seq} ", SequenceWidth = 5 },
					new DocumentNumberPatternUpdate { Kind = DocumentNumberKinds.Bid, Pattern = "BID {SEQ}", SequenceWidth = 3 },
					new DocumentNumberPatternUpdate { Kind = DocumentNumberKinds.Inspection, Pattern = "INS-{SEQ}", SequenceWidth = 3 },
					new DocumentNumberPatternUpdate { Kind = DocumentNumberKinds.Invoice, Pattern = "", SequenceWidth = 4 }
				}
			});

			result.YearStartRejected.Should().BeFalse();
			result.PatternsRejected.Should().Equal(DocumentNumberKinds.Bid);
			_documents.YearStart().Month.Should().Be(11);
			_documents.Patterns.Should().ContainSingle().Which.Pattern.Should().Be("mnt-{YY}-{SEQ}");
			_records.DocumentPatterns.Should().BeEmpty("a Records kind is not saved with setting 117");
		}

		[Test]
		public async Task A_year_start_every_year_does_not_have_saves_nothing()
		{
			Pattern(DocumentNumberKinds.Invoice, "INV-{SEQ}", 4);

			var result = await _service.SaveAsync(Dept, "admin", new DocumentNumberingUpdate
			{
				YearStartMonth = 2, YearStartDay = 29,
				Patterns = { new DocumentNumberPatternUpdate { Kind = DocumentNumberKinds.Invoice, Pattern = "", SequenceWidth = 1 } }
			});

			result.YearStartRejected.Should().BeTrue();
			_documents.Patterns.Should().ContainSingle("nothing was saved");
		}

		[Test]
		public async Task A_raised_next_number_applies_to_the_sequence_shown_and_only_ever_rises()
		{
			Pattern(DocumentNumberKinds.Bid, "B{YYYY}-{SEQ}", 3);
			var shown = (await _service.GetStatusesAsync(Dept, false, DateTime.UtcNow)).Single(s => s.Kind == DocumentNumberKinds.Bid);
			shown.Custom.Should().BeTrue();
			shown.NextSequence.Should().Be(1);

			var raised = await _service.SaveAsync(Dept, "admin", new DocumentNumberingUpdate
			{
				Patterns = { new DocumentNumberPatternUpdate { Kind = DocumentNumberKinds.Bid, Pattern = "B{YYYY}-{SEQ}", SequenceWidth = 3, ScopeKey = shown.ScopeKey, NextSequence = 77 } }
			});
			raised.BelowCurrent.Should().BeEmpty();
			raised.NotApplied.Should().Be(0);
			(await _service.TakeCustomNumberAsync(Dept, DocumentNumberKinds.Bid, DateTime.UtcNow)).Should().EndWith("-077");

			var lowered = await _service.SaveAsync(Dept, "admin", new DocumentNumberingUpdate
			{
				Patterns = { new DocumentNumberPatternUpdate { Kind = DocumentNumberKinds.Bid, Pattern = "B{YYYY}-{SEQ}", SequenceWidth = 3, ScopeKey = shown.ScopeKey, NextSequence = 10 } }
			});
			lowered.BelowCurrent.Should().ContainSingle().Which.NextSequence.Should().Be(78);
		}

		[Test]
		public async Task A_next_number_typed_against_the_old_pattern_is_not_applied_to_the_new_one()
		{
			Pattern(DocumentNumberKinds.Bid, "B{YYYY}-{SEQ}", 3);
			var shown = (await _service.GetStatusesAsync(Dept, false, DateTime.UtcNow)).Single(s => s.Kind == DocumentNumberKinds.Bid);

			var result = await _service.SaveAsync(Dept, "admin", new DocumentNumberingUpdate
			{
				Patterns = { new DocumentNumberPatternUpdate { Kind = DocumentNumberKinds.Bid, Pattern = "BID-{YY}-{SEQ}", SequenceWidth = 3, ScopeKey = shown.ScopeKey, NextSequence = 50 } }
			});

			result.NotApplied.Should().Be(1);
			_sequences.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Records_patterns_save_into_records_numbering_and_follow_its_year_start()
		{
			_records.YearStartMonth = 11;
			_records.YearStartDay = 1;
			_records.Pattern = "{PREFIX}-{YYYY}-{SEQ}";

			var result = await _service.SaveRecordsPatternsAsync(Dept, "admin", new List<DocumentNumberPatternUpdate>
			{
				new DocumentNumberPatternUpdate { Kind = DocumentNumberKinds.RecordsRequest, Pattern = "PRR{YY}-{SEQ}", SequenceWidth = 3 },
				new DocumentNumberPatternUpdate { Kind = DocumentNumberKinds.Invoice, Pattern = "INV-{SEQ}", SequenceWidth = 3 }
			});

			result.PatternsRejected.Should().BeEmpty();
			_records.Pattern.Should().Be("{PREFIX}-{YYYY}-{SEQ}", "the rest of setting 72 is left as it was");
			_records.DocumentPatterns.Should().ContainSingle().Which.Kind.Should().Be(DocumentNumberKinds.RecordsRequest);
			_documents.Patterns.Should().BeEmpty();
			(await _service.TakeCustomNumberAsync(Dept, DocumentNumberKinds.RecordsRequest, new DateTime(2026, 11, 5))).Should().Be("PRR27-001");
		}

		[Test]
		public async Task Statuses_show_a_sample_of_the_built_in_numbers()
		{
			var statuses = await _service.GetStatusesAsync(Dept, false, new DateTime(2026, 6, 1));

			statuses.Select(s => s.Kind).Should().Equal(DocumentNumberKinds.WorkOrder, DocumentNumberKinds.Invoice, DocumentNumberKinds.Bid, DocumentNumberKinds.TimeReport);
			statuses.Single(s => s.Kind == DocumentNumberKinds.WorkOrder).Example.Should().Be("WO-2026-000153");
			statuses.Single(s => s.Kind == DocumentNumberKinds.Invoice).Example.Should().Be("153");
			statuses.Should().OnlyContain(s => !s.Custom && s.NextNumber == null);
			(await _service.GetStatusesAsync(Dept, true, new DateTime(2026, 6, 1))).Single(s => s.Kind == DocumentNumberKinds.Permit).Example.Should().Be("PRM-2026-0153");
		}

		#endregion
	}
}
