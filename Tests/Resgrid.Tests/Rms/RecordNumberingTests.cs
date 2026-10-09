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
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Department record numbering (setting 72): the pattern grammar, the legacy pattern the checkboxes always
	/// produced, the next-number floors and the rule that a next number only ever rises.
	/// </summary>
	[TestFixture]
	public class RecordNumberingTests
	{
		private const int Dept = 9;

		#region Pattern

		[TestCase(true, false, null, "RUN-2026-")]
		[TestCase(true, true, 11, "RUN-G11-2026-")]
		[TestCase(true, true, null, "RUN-2026-")]
		[TestCase(false, false, null, "RUN-")]
		[TestCase(false, true, 11, "RUN-G11-")]
		[TestCase(false, true, null, "RUN-")]
		public void Legacy_pattern_renders_the_prefix_the_checkboxes_always_did(bool includeYear, bool perGroup, int? groupId, string expectedPrefix)
		{
			var scope = RecordNumberFormat.Resolve(RecordNumberFormat.LegacyPattern(includeYear, perGroup), 4, "RUN", 2026, groupId);

			scope.Prefix.Should().Be(expectedPrefix);
			scope.Suffix.Should().BeEmpty();
			scope.Format(7).Should().Be(expectedPrefix + "0007");
		}

		[Test]
		public void A_department_with_no_saved_pattern_uses_the_legacy_one()
		{
			RecordNumberFormat.EffectivePattern(new RecordsNumberingConfig()).Should().Be("{PREFIX}-{YYYY}-{SEQ}");
			RecordNumberFormat.EffectivePattern(new RecordsNumberingConfig { IncludeYear = false, PerGroupSequence = true }).Should().Be("{PREFIX}-{GROUP}-{SEQ}");
			RecordNumberFormat.EffectivePattern(new RecordsNumberingConfig { Pattern = "{yyyy}-{seq}" }).Should().Be("{YYYY}-{SEQ}", "tokens are written back in upper case");
			RecordNumberFormat.EffectivePattern(new RecordsNumberingConfig { Pattern = "no sequence" }).Should().Be("{PREFIX}-{YYYY}-{SEQ}", "a saved pattern that no longer validates never stops numbering");
		}

		[TestCase("{YYYY}-{SEQ}", 2026, "2026-", "")]
		[TestCase("{PREFIX}{YY}-{SEQ}", 2026, "INC26-", "")]
		[TestCase("{SEQ}-{YY}", 2026, "", "-26")]
		[TestCase("FD.{YYYY}.{SEQ}.{PREFIX}", 2027, "FD.2027.", ".INC")]
		[TestCase("{SEQ}", 2026, "", "")]
		public void Patterns_render_text_on_both_sides_of_the_sequence(string pattern, int year, string prefix, string suffix)
		{
			var scope = RecordNumberFormat.Resolve(pattern, 4, "INC", year, null);

			scope.Prefix.Should().Be(prefix);
			scope.Suffix.Should().Be(suffix);
			scope.Key.Should().Be(prefix + "#" + suffix);
		}

		[Test]
		public void An_empty_group_takes_its_separator_along()
		{
			RecordNumberFormat.Resolve("{GROUP}-{YYYY}-{SEQ}", 4, "RUN", 2026, null).Format(1).Should().Be("2026-0001");
			RecordNumberFormat.Resolve("{GROUP}-{YYYY}-{SEQ}", 4, "RUN", 2026, 4).Format(1).Should().Be("G4-2026-0001");
			RecordNumberFormat.Resolve("{PREFIX}_{GROUP}.{SEQ}", 3, "RUN", 2026, null).Format(1).Should().Be("RUN_001");
		}

		[TestCase("")]
		[TestCase("   ")]
		[TestCase("{PREFIX}-{YYYY}", Description = "no sequence")]
		[TestCase("{SEQ}-{SEQ}", Description = "sequence twice")]
		[TestCase("{YYYY}-{YYYY}-{SEQ}", Description = "token twice")]
		[TestCase("{CALL}-{SEQ}", Description = "unknown token")]
		[TestCase("{SEQ", Description = "unclosed token")]
		[TestCase("INC {SEQ}", Description = "space")]
		[TestCase("INC/{SEQ}", Description = "slash")]
		[TestCase("INC#{SEQ}", Description = "the scope key marker")]
		[TestCase("{GROUP}{SEQ}", Description = "group runs into the sequence")]
		[TestCase("{PREFIX}-{GROUP}", Description = "group at the end and no sequence")]
		[TestCase("{SEQ}-{GROUP}", Description = "group at the end")]
		[TestCase("{GROUP}0{SEQ}", Description = "group followed by a digit")]
		[TestCase("ABCDEFGHIJKLMNOPQRSTUVWXYZ-ABCDEFGH-{SEQ}", Description = "longer than 40")]
		public void Invalid_patterns_are_refused(string pattern)
		{
			RecordNumberFormat.IsValid(pattern).Should().BeFalse();
			RecordNumberFormat.Normalize(pattern).Should().BeNull();
		}

		[Test]
		public void The_longest_valid_pattern_still_fits_the_record_number_column()
		{
			var pattern = "ABCDEFGHIJKL.{PREFIX}{YYYY}{GROUP}-{SEQ}";
			pattern.Length.Should().Be(RecordNumberFormat.MaxPatternLength);
			RecordNumberFormat.IsValid(pattern).Should().BeTrue();

			RecordNumberFormat.Resolve(pattern, RecordNumberFormat.MaxWidth, "INC", 2026, int.MaxValue).Format(int.MaxValue - 1).Length.Should().BeLessThanOrEqualTo(50);
		}

		[TestCase("IR")]
		[TestCase("FIRE")]
		[TestCase("E12")]
		[TestCase("ABCDEF")]
		public void Valid_prefixes_are_accepted(string prefix)
		{
			RecordNumberFormat.IsValidPrefix(prefix).Should().BeTrue();
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("R", Description = "shorter than 2")]
		[TestCase("ABCDEFG", Description = "longer than 6")]
		[TestCase("fire", Description = "lower case is upper-cased before it is saved")]
		[TestCase("FD-RUN", Description = "separators belong to the pattern")]
		[TestCase("FD RUN", Description = "space")]
		[TestCase("RUN#", Description = "the scope key marker")]
		[TestCase("ÉVAC", Description = "outside ASCII")]
		[TestCase("{SEQ}", Description = "a token")]
		public void Invalid_prefixes_are_refused(string prefix)
		{
			RecordNumberFormat.IsValidPrefix(prefix).Should().BeFalse();
		}

		[Test]
		public void A_department_prefix_replaces_the_default_for_its_type_only()
		{
			var config = new RecordsNumberingConfig();
			config.SetPrefix(RmsDefinitionKeys.Run, "FIRE");
			config.SetPrefix(RmsDefinitionKeys.NerisIncidentReport, "IR");

			config.PrefixFor(RmsDefinitionKeys.Run).Should().Be("FIRE");
			config.PrefixFor(RmsDefinitionKeys.NerisIncidentReport).Should().Be("IR");
			config.PrefixFor(RmsDefinitionKeys.Training).Should().Be("TRN");
			new RecordsNumberingConfig().PrefixFor(RmsDefinitionKeys.NerisIncidentReport).Should().Be("INC");
		}

		[Test]
		public void Setting_a_prefix_back_to_blank_or_the_default_removes_the_department_one()
		{
			var config = new RecordsNumberingConfig();
			config.SetPrefix(RmsDefinitionKeys.Run, "FIRE");
			config.SetPrefix(RmsDefinitionKeys.Run, "R2");
			config.Prefixes.Should().ContainSingle().Which.Prefix.Should().Be("R2");

			config.SetPrefix(RmsDefinitionKeys.Run, "RUN");
			config.Prefixes.Should().BeEmpty("the default is not stored, so a later change to the default still reaches the type");

			config.SetPrefix(RmsDefinitionKeys.Run, "FIRE");
			config.SetPrefix(RmsDefinitionKeys.Run, null);
			config.Prefixes.Should().BeEmpty();
			config.PrefixFor(RmsDefinitionKeys.Run).Should().Be("RUN");
		}

		[Test]
		public void A_saved_prefix_that_no_longer_validates_falls_back_to_the_default()
		{
			var config = new RecordsNumberingConfig { Prefixes = { new RecordsNumberingPrefix { DefinitionKey = RmsDefinitionKeys.Run, Prefix = "FIRE-1" } } };

			config.PrefixFor(RmsDefinitionKeys.Run).Should().Be("RUN", "a bad stored value never stops numbering");
		}

		[Test]
		public void The_longest_prefix_still_fits_the_record_number_column()
		{
			var pattern = "ABCDEFGHIJKL.{PREFIX}{YYYY}{GROUP}-{SEQ}";

			RecordNumberFormat.Resolve(pattern, RecordNumberFormat.MaxWidth, "ABCDEF", 2026, int.MaxValue).Format(int.MaxValue - 1).Length.Should().BeLessThanOrEqualTo(50);
		}

		[Test]
		public void The_manifest_lists_the_department_prefixes()
		{
			var config = new RecordsNumberingConfig();
			config.SetPrefix(RmsDefinitionKeys.Run, "FIRE");
			config.SetPrefix(RmsDefinitionKeys.NerisIncidentReport, "IR");

			var definitions = RecordDefinitionCatalog.Describe(config);

			definitions.Single(d => d.Key == RmsDefinitionKeys.Run).NumberPrefix.Should().Be("FIRE");
			definitions.Single(d => d.Key == RmsDefinitionKeys.NerisIncidentReport).NumberPrefix.Should().Be("IR");
			definitions.Single(d => d.Key == RmsDefinitionKeys.Meeting).NumberPrefix.Should().Be("MTG");
			RecordDefinitionCatalog.Describe().Single(d => d.Key == RmsDefinitionKeys.NerisIncidentReport).NumberPrefix.Should().Be("INC");
		}

		[Test]
		public void Year_tokens_are_what_restart_the_sequence()
		{
			RecordNumberFormat.ResetsYearly("{PREFIX}-{YYYY}-{SEQ}").Should().BeTrue();
			RecordNumberFormat.ResetsYearly("{YY}{SEQ}").Should().BeTrue();
			RecordNumberFormat.ResetsYearly("{PREFIX}-{SEQ}").Should().BeFalse();
		}

		#endregion

		#region Floors

		[Test]
		public void Next_sequence_is_one_past_the_highest_issued_but_never_below_the_floor()
		{
			var config = new RecordsNumberingConfig();
			config.NextSequence("2026-#", 0).Should().Be(1);
			config.NextSequence("2026-#", 41).Should().Be(42);

			config.RaiseFloor("2026-#", 153, "admin", DateTime.UtcNow);

			config.NextSequence("2026-#", 0).Should().Be(153, "the department already issued 2026-0001 through 2026-0152 elsewhere");
			config.NextSequence("2026-#", 200).Should().Be(201, "numbers that exist always win over the floor");
			config.NextSequence("2027-#", 0).Should().Be(1, "a floor belongs to its own sequence");
		}

		[Test]
		public void A_floor_only_rises()
		{
			var config = new RecordsNumberingConfig();
			config.RaiseFloor("INC-2026-#", 153, "admin", DateTime.UtcNow);
			config.RaiseFloor("INC-2026-#", 100, "admin", DateTime.UtcNow);
			config.FloorFor("INC-2026-#").Should().Be(153);

			config.RaiseFloor("INC-2026-#", 500, "admin", DateTime.UtcNow);
			config.FloorFor("INC-2026-#").Should().Be(500);
			config.Floors.Should().ContainSingle();
		}

		[Test]
		public void Pattern_and_floors_survive_the_setting_serializer()
		{
			var config = new RecordsNumberingConfig { Pattern = "{YYYY}-{SEQ}", SequenceWidth = 5 };
			config.RaiseFloor("2026-#", 153, "admin", new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
			config.SetPrefix(RmsDefinitionKeys.Run, "FIRE");

			var copy = ObjectSerialization.Deserialize<RecordsNumberingConfig>(ObjectSerialization.Serialize(config));

			copy.Pattern.Should().Be("{YYYY}-{SEQ}");
			copy.SequenceWidth.Should().Be(5);
			copy.Floors.Should().ContainSingle();
			copy.FloorFor("2026-#").Should().Be(153);
			copy.Floors[0].SetByUserId.Should().Be("admin");
			copy.PrefixFor(RmsDefinitionKeys.Run).Should().Be("FIRE");
		}

		[Test]
		public void A_setting_saved_before_patterns_existed_reads_back_with_no_pattern_and_no_floors()
		{
			var legacy = ObjectSerialization.Deserialize<RecordsNumberingConfig>(ObjectSerialization.Serialize(new RecordsNumberingConfig { PerGroupSequence = true }));

			legacy.Pattern.Should().BeNull();
			legacy.Floors.Should().BeEmpty();
			legacy.Prefixes.Should().BeEmpty();
			legacy.PrefixFor(RmsDefinitionKeys.Run).Should().Be("RUN");
			RecordNumberFormat.EffectivePattern(legacy).Should().Be("{PREFIX}-{GROUP}-{YYYY}-{SEQ}");
		}

		#endregion

		#region Repository rule (fake)

		[Test]
		public void Only_digits_between_prefix_and_suffix_count_as_a_sequence()
		{
			var numbers = new[] { "RUN-2026-0007", "RUN-2026-12", "RUN-G4-2026-0099", "RUN-2026-0005-X", "2026-0300", null };

			FakeRmsStore.MaxRecordNumberSequence(numbers, "RUN-2026-", "").Should().Be(12, "the sequence is read as a number, so a width change never restarts it");
			FakeRmsStore.MaxRecordNumberSequence(numbers, "RUN-", "").Should().Be(0, "RUN-2026-0007 is not RUN- followed only by digits");
			FakeRmsStore.MaxRecordNumberSequence(numbers, "RUN-2026-", "-X").Should().Be(5);
		}

		#endregion

		#region Settings service

		private RecordsNumberingConfig _saved;
		private List<string> _issued;
		private RecordsNumberingService _service;

		[SetUp]
		public void SetUp()
		{
			_saved = new RecordsNumberingConfig();
			_issued = new List<string>();
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetRecordsNumberingConfigAsync(Dept, It.IsAny<bool>()))
				.ReturnsAsync(() => ObjectSerialization.Deserialize<RecordsNumberingConfig>(ObjectSerialization.Serialize(_saved)));
			settings.Setup(s => s.SetRecordsNumberingConfigAsync(Dept, It.IsAny<RecordsNumberingConfig>(), It.IsAny<CancellationToken>()))
				.Callback((int d, RecordsNumberingConfig c, CancellationToken t) => _saved = c)
				.ReturnsAsync(new DepartmentSetting());
			var records = new Mock<IRmsOperationalRecordsRepository>();
			records.Setup(r => r.GetMaxRecordNumberSequenceAsync(Dept, It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((int d, string prefix, string suffix) => FakeRmsStore.MaxRecordNumberSequence(_issued, prefix, suffix));
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetAllGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup>
			{
				new DepartmentGroup { DepartmentGroupId = 12, Name = "Station 2" },
				new DepartmentGroup { DepartmentGroupId = 11, Name = "Station 1" }
			});
			_service = new RecordsNumberingService(settings.Object, records.Object, groups.Object);
		}

		[Test]
		public async Task Sequences_list_every_numbered_type_with_what_it_issues_next()
		{
			_issued.Add("TRN-2026-0004");

			var sequences = await _service.GetSequencesAsync(Dept, new RecordsNumberingConfig(), 2026);

			sequences.Should().HaveCount(RecordsNumberingService.NumberedTypes().Count());
			sequences.Single(s => s.DefinitionKeys.Contains(RmsDefinitionKeys.Training)).NextNumber.Should().Be("TRN-2026-0005");
			sequences.Single(s => s.DefinitionKeys.Contains(RmsDefinitionKeys.NerisIncidentReport)).NextNumber.Should().Be("INC-2026-0001");
		}

		[Test]
		public async Task A_pattern_without_a_prefix_is_one_shared_sequence()
		{
			_issued.Add("2026-0009");

			var sequences = await _service.GetSequencesAsync(Dept, new RecordsNumberingConfig { Pattern = "{YYYY}-{SEQ}" }, 2026);

			sequences.Should().ContainSingle();
			sequences[0].DefinitionKeys.Should().HaveCount(RecordsNumberingService.NumberedTypes().Count());
			sequences[0].NextNumber.Should().Be("2026-0010");
		}

		[Test]
		public async Task A_group_pattern_lists_a_sequence_per_group_and_one_for_records_without_a_group()
		{
			var sequences = await _service.GetSequencesAsync(Dept, new RecordsNumberingConfig { Pattern = "{GROUP}-{YYYY}-{SEQ}" }, 2026);

			sequences.Select(s => s.GroupId).Should().Equal(null, 11, 12);
			sequences.Select(s => s.NextNumber).Should().Equal("2026-0001", "G11-2026-0001", "G12-2026-0001");
		}

		[Test]
		public async Task Saving_raises_a_next_number_for_a_department_moving_over_mid_year()
		{
			var result = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{yyyy}-{seq}",
				SequenceWidth = 4,
				Year = 2026,
				NextNumbers = { new RecordNextNumberRequest { ScopeKey = "2026-#", NextSequence = 153 } }
			});

			result.PatternRejected.Should().BeFalse();
			result.BelowCurrent.Should().BeEmpty();
			result.NotApplied.Should().Be(0);
			_saved.Pattern.Should().Be("{YYYY}-{SEQ}");
			_saved.IncludeYear.Should().BeTrue();
			_saved.ResetYearly.Should().BeTrue();
			_saved.PerGroupSequence.Should().BeFalse();
			(await _service.GetSequencesAsync(Dept, _saved, 2026)).Single().NextNumber.Should().Be("2026-0153");
		}

		[Test]
		public async Task A_next_number_below_what_the_sequence_would_issue_is_refused()
		{
			_saved.Pattern = "{YYYY}-{SEQ}";
			_saved.RaiseFloor("2026-#", 153, "admin", DateTime.UtcNow);

			var result = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{YYYY}-{SEQ}",
				SequenceWidth = 4,
				Year = 2026,
				NextNumbers = { new RecordNextNumberRequest { ScopeKey = "2026-#", NextSequence = 20 } }
			});

			result.BelowCurrent.Should().ContainSingle().Which.NextNumber.Should().Be("2026-0153");
			_saved.FloorFor("2026-#").Should().Be(153, "a lower next number would re-issue numbers the department already used");
		}

		[Test]
		public async Task A_next_number_below_an_issued_number_is_refused_even_without_a_floor()
		{
			_issued.Add("TRN-2026-0040");

			var result = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{PREFIX}-{YYYY}-{SEQ}",
				SequenceWidth = 4,
				Year = 2026,
				NextNumbers = { new RecordNextNumberRequest { ScopeKey = "TRN-2026-#", NextSequence = 40 } }
			});

			result.BelowCurrent.Should().ContainSingle();
			_saved.Floors.Should().BeEmpty();
		}

		[Test]
		public async Task An_invalid_pattern_saves_nothing()
		{
			_saved.Pattern = "{PREFIX}-{YYYY}-{SEQ}";

			var result = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{PREFIX} {YYYY}",
				SequenceWidth = 6,
				Year = 2026,
				NextNumbers = { new RecordNextNumberRequest { ScopeKey = "RUN-2026-#", NextSequence = 99 } }
			});

			result.PatternRejected.Should().BeTrue();
			_saved.Pattern.Should().Be("{PREFIX}-{YYYY}-{SEQ}");
			_saved.SequenceWidth.Should().Be(4);
			_saved.Floors.Should().BeEmpty();
		}

		[Test]
		public async Task A_next_number_typed_against_the_old_pattern_is_not_carried_to_the_new_one()
		{
			var result = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{YYYY}-{SEQ}",
				SequenceWidth = 4,
				Year = 2026,
				NextNumbers = { new RecordNextNumberRequest { ScopeKey = "RUN-2026-#", NextSequence = 153 } }
			});

			result.NotApplied.Should().Be(1);
			_saved.Pattern.Should().Be("{YYYY}-{SEQ}", "the pattern itself still saves");
			_saved.Floors.Should().BeEmpty();
		}

		[Test]
		public async Task Sequences_use_the_department_prefixes()
		{
			_issued.Add("FIRE-2026-0007");
			var config = new RecordsNumberingConfig();
			config.SetPrefix(RmsDefinitionKeys.Run, "FIRE");
			config.SetPrefix(RmsDefinitionKeys.NerisIncidentReport, "IR");

			var sequences = await _service.GetSequencesAsync(Dept, config, 2026);

			sequences.Single(s => s.DefinitionKeys.Contains(RmsDefinitionKeys.Run)).NextNumber.Should().Be("FIRE-2026-0008");
			sequences.Single(s => s.DefinitionKeys.Contains(RmsDefinitionKeys.NerisIncidentReport)).NextNumber.Should().Be("IR-2026-0001");
			sequences.Single(s => s.DefinitionKeys.Contains(RmsDefinitionKeys.Training)).NextNumber.Should().Be("TRN-2026-0001");
		}

		[Test]
		public async Task Types_given_the_same_prefix_share_one_sequence()
		{
			_issued.Add("FD-2026-0011");
			var config = new RecordsNumberingConfig();
			config.SetPrefix(RmsDefinitionKeys.Run, "FD");
			config.SetPrefix(RmsDefinitionKeys.NerisIncidentReport, "FD");

			var sequences = await _service.GetSequencesAsync(Dept, config, 2026);

			sequences.Should().HaveCount(RecordsNumberingService.NumberedTypes().Count() - 1);
			var shared = sequences.Single(s => s.ScopeKey == "FD-2026-#");
			shared.DefinitionKeys.Should().BeEquivalentTo(RmsDefinitionKeys.Run, RmsDefinitionKeys.NerisIncidentReport);
			shared.NextNumber.Should().Be("FD-2026-0012");
		}

		[Test]
		public async Task Saving_stores_prefixes_upper_cased_and_a_blank_one_returns_the_type_to_its_default()
		{
			_saved.SetPrefix(RmsDefinitionKeys.Training, "DRILL");

			var result = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{PREFIX}-{YYYY}-{SEQ}",
				SequenceWidth = 4,
				Year = 2026,
				Prefixes = new List<RecordNumberPrefixRequest>
				{
					new RecordNumberPrefixRequest { DefinitionKey = RmsDefinitionKeys.Run, Prefix = " fire " },
					new RecordNumberPrefixRequest { DefinitionKey = RmsDefinitionKeys.NerisIncidentReport, Prefix = "ir" },
					new RecordNumberPrefixRequest { DefinitionKey = RmsDefinitionKeys.Training, Prefix = "" }
				}
			});

			result.PrefixesRejected.Should().BeEmpty();
			_saved.PrefixFor(RmsDefinitionKeys.Run).Should().Be("FIRE");
			_saved.PrefixFor(RmsDefinitionKeys.NerisIncidentReport).Should().Be("IR");
			_saved.PrefixFor(RmsDefinitionKeys.Training).Should().Be("TRN");
			_saved.Prefixes.Should().HaveCount(2);
		}

		[Test]
		public async Task An_invalid_prefix_keeps_the_one_the_type_had_and_the_rest_still_saves()
		{
			_saved.SetPrefix(RmsDefinitionKeys.Run, "FIRE");

			var result = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{PREFIX}-{YY}-{SEQ}",
				SequenceWidth = 4,
				Year = 2026,
				Prefixes = new List<RecordNumberPrefixRequest>
				{
					new RecordNumberPrefixRequest { DefinitionKey = RmsDefinitionKeys.Run, Prefix = "FD-RUN" },
					new RecordNumberPrefixRequest { DefinitionKey = RmsDefinitionKeys.Meeting, Prefix = "MEET" }
				}
			});

			result.PrefixesRejected.Should().Equal(RmsDefinitionKeys.Run);
			_saved.Pattern.Should().Be("{PREFIX}-{YY}-{SEQ}");
			_saved.PrefixFor(RmsDefinitionKeys.Run).Should().Be("FIRE");
			_saved.PrefixFor(RmsDefinitionKeys.Meeting).Should().Be("MEET");
		}

		[Test]
		public async Task Prefixes_are_only_saved_for_the_system_types_the_pattern_numbers()
		{
			var result = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{PREFIX}-{YYYY}-{SEQ}",
				SequenceWidth = 4,
				Year = 2026,
				Prefixes = new List<RecordNumberPrefixRequest> { new RecordNumberPrefixRequest { DefinitionKey = "dept.ics-214", Prefix = "ICS" } }
			});

			result.PrefixesRejected.Should().BeEmpty();
			_saved.Prefixes.Should().BeEmpty("department definitions carry their prefix on the definition");
		}

		[Test]
		public async Task Saving_without_prefixes_leaves_the_saved_ones_alone()
		{
			_saved.SetPrefix(RmsDefinitionKeys.Run, "FIRE");

			await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate { Pattern = "{PREFIX}-{YYYY}-{SEQ}", SequenceWidth = 4, Year = 2026 });

			_saved.PrefixFor(RmsDefinitionKeys.Run).Should().Be("FIRE");
		}

		[Test]
		public async Task A_next_number_typed_against_the_old_prefix_is_not_carried_to_the_new_one()
		{
			var result = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{PREFIX}-{YYYY}-{SEQ}",
				SequenceWidth = 4,
				Year = 2026,
				Prefixes = new List<RecordNumberPrefixRequest> { new RecordNumberPrefixRequest { DefinitionKey = RmsDefinitionKeys.Run, Prefix = "FIRE" } },
				NextNumbers =
				{
					new RecordNextNumberRequest { ScopeKey = "RUN-2026-#", NextSequence = 153 },
					new RecordNextNumberRequest { ScopeKey = "TRN-2026-#", NextSequence = 40 }
				}
			});

			result.NotApplied.Should().Be(1, "RUN-2026 is no longer a sequence once Run numbers as FIRE");
			_saved.FloorFor("RUN-2026-#").Should().Be(1);
			_saved.FloorFor("FIRE-2026-#").Should().Be(1);
			_saved.FloorFor("TRN-2026-#").Should().Be(40, "a type whose prefix did not change keeps its raised number");
		}

		[Test]
		public async Task Saving_stores_the_year_start_and_refuses_a_day_not_every_year_has()
		{
			var saved = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{PREFIX}{YY}-{SEQ}", SequenceWidth = 4, Year = 2026, YearStartMonth = 11, YearStartDay = 1, YearLabel = (int)NumberingYearLabel.EndYear
			});
			saved.YearStartRejected.Should().BeFalse();
			_saved.YearStart().Month.Should().Be(11);
			_saved.YearStart().Day.Should().Be(1);
			_saved.YearStart().Label.Should().Be(NumberingYearLabel.EndYear);

			var leap = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate { Pattern = "{PREFIX}-{SEQ}", SequenceWidth = 6, Year = 2026, YearStartMonth = 2, YearStartDay = 29 });
			leap.YearStartRejected.Should().BeTrue();
			_saved.Pattern.Should().Be("{PREFIX}{YY}-{SEQ}", "a refused year start saves nothing in setting 72");

			await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate { Pattern = "{PREFIX}{YY}-{SEQ}", SequenceWidth = 4, Year = 2026 });
			_saved.YearStart().Month.Should().Be(11, "a save that does not post a year start keeps the saved one");
		}

		[Test]
		public async Task A_next_number_typed_before_the_year_start_changed_is_not_applied()
		{
			var result = await _service.SaveAsync(Dept, "admin", new RecordsNumberingUpdate
			{
				Pattern = "{PREFIX}-{YYYY}-{SEQ}",
				SequenceWidth = 4,
				Year = 2026,
				YearStartMonth = 7,
				YearStartDay = 1,
				NextNumbers = { new RecordNextNumberRequest { ScopeKey = "TRN-2026-#", NextSequence = 40 } }
			});

			result.NotApplied.Should().Be(1, "the next numbers shown were the old year's sequences");
			_saved.FloorFor("TRN-2026-#").Should().Be(1);
			_saved.YearStart().Month.Should().Be(7, "the year start itself still saves");
		}

		[Test]
		public async Task Sequences_are_listed_for_a_fiscal_year_by_its_name()
		{
			_issued.Add("EZKT27-0004");
			var config = new RecordsNumberingConfig { Pattern = "{PREFIX}{YY}-{SEQ}", YearStartMonth = 11, YearStartDay = 1 };
			config.SetPrefix(RmsDefinitionKeys.NerisIncidentReport, "EZKT");

			var year = RecordsNumberingService.NumberingYear(config, new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc), null);
			var sequences = await _service.GetSequencesAsync(Dept, config, year);

			year.Should().Be(2027);
			sequences.Single(s => s.DefinitionKeys.Contains(RmsDefinitionKeys.NerisIncidentReport)).NextNumber.Should().Be("EZKT27-0005");
		}

		[TestCase(2027, 1, 1, 3, 0, null, 2027, "no time zone reads the date as UTC")]
		[TestCase(2027, 1, 1, 3, 0, "Central Standard Time", 2026, "9 PM on December 31 in Chicago is still 2026")]
		[TestCase(2027, 1, 1, 7, 0, "Central Standard Time", 2027, "1 AM on January 1 in Chicago")]
		public void A_records_numbering_year_is_the_departments_local_year(int y, int m, int d, int h, int min, string timeZone, int expected, string because)
		{
			RecordsNumberingService.NumberingYear(new RecordsNumberingConfig(), new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc), timeZone).Should().Be(expected, because);
		}

		[Test]
		public void The_records_year_start_round_trips_through_protobuf()
		{
			var copy = ObjectSerialization.Deserialize<RecordsNumberingConfig>(ObjectSerialization.Serialize(
				new RecordsNumberingConfig { YearStartMonth = 10, YearStartDay = 1, YearLabel = (int)NumberingYearLabel.StartYear }));

			copy.YearStart().Month.Should().Be(10);
			copy.YearStart().Label.Should().Be(NumberingYearLabel.StartYear);
			ObjectSerialization.Deserialize<RecordsNumberingConfig>(ObjectSerialization.Serialize(new RecordsNumberingConfig())).YearStart().IsCalendarYear.Should().BeTrue();
		}

		#endregion
	}
}
