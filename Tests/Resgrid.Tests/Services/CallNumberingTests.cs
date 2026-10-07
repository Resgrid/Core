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
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Department call numbering (setting 115 + CallNumberSequences): the pattern grammar, the legacy "26-153" numbers a
	/// department keeps until it saves its own pattern, sequence seeding from numbers already issued, raised next numbers
	/// that only ever rise, and renumbering a year.
	/// </summary>
	[TestFixture]
	public class CallNumberingTests
	{
		private const int Dept = 7;

		#region Pattern

		[Test]
		public void A_department_with_no_saved_pattern_keeps_the_legacy_numbers()
		{
			var scope = CallNumberFormat.Resolve(CallNumberFormat.EffectivePattern(new CallNumberingConfig()), 0, new DateTime(2026, 10, 6));

			scope.Prefix.Should().Be("26-");
			scope.Suffix.Should().BeEmpty();
			scope.Format(153).Should().Be("26-153", "the legacy numbers are unpadded");
			scope.Period.Should().Be(CallNumberResetPeriod.Yearly);
		}

		[TestCase("FD{YYYY}-{SEQ}", 4, "FD2026-0007")]
		[TestCase("{YY}{MM}{DD}-{SEQ}", 3, "261006-007")]
		[TestCase("{SEQ}/{YYYY}", 5, "00007/2026")]
		[TestCase("CAD.{YYYY}.{MM}.{SEQ}", 1, "CAD.2026.10.7")]
		[TestCase("{SEQ}", 6, "000007")]
		[TestCase("{yyyy}_{seq}", 2, "2026_07")]
		public void Patterns_write_their_date_parts_around_the_sequence(string pattern, int width, string expected)
		{
			CallNumberFormat.Resolve(pattern, width, new DateTime(2026, 10, 6, 14, 30, 0)).Format(7).Should().Be(expected);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("FD-{YYYY}")]
		[TestCase("{SEQ}-{SEQ}")]
		[TestCase("{YYYY}-{YYYY}-{SEQ}")]
		[TestCase("{MM}-{SEQ}")]
		[TestCase("{YYYY}-{DD}-{SEQ}")]
		[TestCase("FD {SEQ}")]
		[TestCase("FD#{SEQ}")]
		[TestCase("{GROUP}-{SEQ}")]
		[TestCase("{SEQ}-ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")]
		public void Invalid_patterns_are_refused(string pattern)
		{
			CallNumberFormat.IsValid(pattern).Should().BeFalse();
			CallNumberFormat.Normalize(pattern).Should().BeNull();
		}

		[Test]
		public void Tokens_are_matched_in_any_case_and_written_back_in_upper_case()
		{
			CallNumberFormat.Normalize(" fd{yy}-{Seq} ").Should().Be("fd{YY}-{SEQ}");
			CallNumberFormat.EffectivePattern(new CallNumberingConfig { Pattern = "nope" }).Should().Be(CallNumberFormat.LegacyPattern, "a saved pattern that no longer validates never stops numbering");
		}

		[TestCase("{SEQ}", CallNumberResetPeriod.Never)]
		[TestCase("{YY}-{SEQ}", CallNumberResetPeriod.Yearly)]
		[TestCase("{YYYY}{MM}-{SEQ}", CallNumberResetPeriod.Monthly)]
		[TestCase("{YYYY}{MM}{DD}-{SEQ}", CallNumberResetPeriod.Daily)]
		public void The_finest_date_part_decides_when_the_sequence_restarts(string pattern, CallNumberResetPeriod period)
		{
			CallNumberFormat.ResetPeriod(pattern).Should().Be(period);
		}

		[Test]
		public void A_scope_covers_exactly_its_local_period()
		{
			var scope = CallNumberFormat.Resolve("{YYYY}{MM}-{SEQ}", 4, new DateTime(2026, 2, 17, 9, 0, 0));

			scope.PeriodStart.Should().Be(new DateTime(2026, 2, 1));
			scope.PeriodEnd.Should().Be(new DateTime(2026, 3, 1));
			CallNumberFormat.Resolve("{SEQ}", 4, new DateTime(2026, 2, 17)).PeriodStart.Should().BeNull();
		}

		[Test]
		public void The_longest_number_fits_the_column_budget()
		{
			var pattern = "{YYYY}{MM}{DD}" + new string('A', CallNumberFormat.MaxPatternLength - "{YYYY}{MM}{DD}{SEQ}".Length) + "{SEQ}";
			CallNumberFormat.IsValid(pattern).Should().BeTrue();
			CallNumberFormat.Resolve(pattern, CallNumberFormat.MaxWidth, DateTime.UtcNow).Format(CallNumberFormat.MaxSequence).Length.Should().BeLessThanOrEqualTo(50);
		}

		[Test]
		public void Config_round_trips_through_protobuf()
		{
			var copy = ObjectSerialization.Deserialize<CallNumberingConfig>(ObjectSerialization.Serialize(new CallNumberingConfig { Pattern = "FD{YYYY}-{SEQ}", SequenceWidth = 1 }));

			copy.Pattern.Should().Be("FD{YYYY}-{SEQ}");
			CallNumberFormat.EffectiveWidth(copy.SequenceWidth).Should().Be(1);
			CallNumberFormat.EffectiveWidth(ObjectSerialization.Deserialize<CallNumberingConfig>(ObjectSerialization.Serialize(new CallNumberingConfig())).SequenceWidth).Should().Be(1);
		}

		#endregion

		#region Service

		private CallNumberingConfig _saved;
		private FakeSequences _sequences;
		private List<Call> _calls;
		private Department _department;
		private CallNumberingService _service;

		[SetUp]
		public void SetUp()
		{
			_saved = new CallNumberingConfig();
			_sequences = new FakeSequences();
			_calls = new List<Call>();
			_department = new Department { DepartmentId = Dept };

			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetCallNumberingConfigAsync(Dept, It.IsAny<bool>()))
				.ReturnsAsync(() => ObjectSerialization.Deserialize<CallNumberingConfig>(ObjectSerialization.Serialize(_saved)));
			settings.Setup(s => s.SetCallNumberingConfigAsync(Dept, It.IsAny<CallNumberingConfig>(), It.IsAny<CancellationToken>()))
				.Callback((int d, CallNumberingConfig c, CancellationToken t) => _saved = c)
				.ReturnsAsync(new DepartmentSetting());
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => _department);
			var calls = new Mock<ICallsRepository>();
			calls.Setup(c => c.GetAllCallsByDepartmentDateRangeAsync(Dept, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync((int d, DateTime start, DateTime end) => _calls.Where(c => !c.IsDeleted && c.LoggedOn >= start && c.LoggedOn <= end).ToList());
			calls.Setup(c => c.SaveOrUpdateAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, CancellationToken t, bool f) => c);
			_sequences.Calls = _calls;

			_service = new CallNumberingService(settings.Object, departments.Object, _sequences, calls.Object);
		}

		private void Issued(string number, DateTime loggedOnUtc, bool deleted = false)
		{
			_calls.Add(new Call { CallId = _calls.Count + 1, DepartmentId = Dept, Number = number, LoggedOn = loggedOnUtc, IsDeleted = deleted });
		}

		[Test]
		public async Task Legacy_numbers_carry_on_from_the_highest_already_issued()
		{
			for (var i = 1; i <= 153; i++)
				Issued("26-" + i, new DateTime(2026, 1, 1).AddHours(i));
			Issued("25-900", new DateTime(2025, 12, 31, 12, 0, 0));

			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2026, 10, 6, 12, 0, 0))).Should().Be("26-154");
			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2026, 10, 6, 12, 1, 0))).Should().Be("26-155");
			_sequences.SeedScans.Should().Be(1, "only the first call of a scope reads the numbers already issued");
		}

		[Test]
		public async Task Deleted_calls_keep_their_numbers()
		{
			Issued("26-1", new DateTime(2026, 1, 2));
			Issued("26-2", new DateTime(2026, 1, 3), deleted: true);

			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2026, 3, 1))).Should().Be("26-3");
		}

		[Test]
		public async Task A_new_pattern_starts_its_own_sequence()
		{
			Issued("26-40", new DateTime(2026, 5, 1));
			_saved = new CallNumberingConfig { Pattern = "FD{YYYY}-{SEQ}", SequenceWidth = 4 };

			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2026, 10, 6))).Should().Be("FD2026-0001");
			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2026, 10, 6))).Should().Be("FD2026-0002");
		}

		[Test]
		public async Task A_daily_pattern_restarts_each_local_day()
		{
			_saved = new CallNumberingConfig { Pattern = "{YYYY}{MM}{DD}-{SEQ}", SequenceWidth = 3 };

			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2026, 10, 6, 8, 0, 0))).Should().Be("20261006-001");
			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2026, 10, 6, 23, 0, 0))).Should().Be("20261006-002");
			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2026, 10, 7, 0, 5, 0))).Should().Be("20261007-001");
		}

		[Test]
		public async Task Dates_are_the_departments_local_date()
		{
			_department.TimeZone = "Central Standard Time";

			// 05:30 UTC on 1 January is still 31 December in Chicago.
			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2027, 1, 1, 5, 30, 0, DateTimeKind.Utc))).Should().Be("26-1");
			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2027, 1, 1, 6, 30, 0, DateTimeKind.Utc))).Should().Be("27-1");
		}

		[Test]
		public async Task Next_number_preview_does_not_take_a_number()
		{
			Issued("26-9", new DateTime(2026, 4, 1));

			(await _service.GetNextAsync(Dept, null, new DateTime(2026, 10, 6))).NextNumber.Should().Be("26-10");
			(await _service.GetNextAsync(Dept, null, new DateTime(2026, 10, 6))).NextNumber.Should().Be("26-10");
			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2026, 10, 6))).Should().Be("26-10");
		}

		[Test]
		public async Task A_raised_next_number_is_issued_next_and_only_ever_rises()
		{
			Issued("26-12", new DateTime(2026, 4, 1));
			var now = DateTime.UtcNow;
			var current = await _service.GetNextAsync(Dept, null, now);

			var raised = await _service.SaveAsync(Dept, "admin", new CallNumberingUpdate { Pattern = CallNumberFormat.LegacyPattern, ScopeKey = current.ScopeKey, NextSequence = 500 });
			raised.BelowCurrent.Should().BeNull();
			raised.NextNotApplied.Should().BeFalse();
			(await _service.GetNextAsync(Dept, null, now)).NextSequence.Should().Be(500);

			var lowered = await _service.SaveAsync(Dept, "admin", new CallNumberingUpdate { Pattern = CallNumberFormat.LegacyPattern, ScopeKey = current.ScopeKey, NextSequence = 200 });
			lowered.BelowCurrent.Should().NotBeNull();
			lowered.BelowCurrent.NextSequence.Should().Be(500);
			(await _service.AllocateCallNumberAsync(Dept, now)).Should().EndWith("-500");
			_sequences.Rows.Single().Value.FloorSetByUserId.Should().Be("admin");
		}

		[Test]
		public async Task A_next_number_typed_against_the_old_pattern_is_not_applied_to_a_new_one()
		{
			var current = await _service.GetNextAsync(Dept, null, DateTime.UtcNow);

			var result = await _service.SaveAsync(Dept, "admin", new CallNumberingUpdate { Pattern = "FD{YYYY}-{SEQ}", SequenceWidth = 4, ScopeKey = current.ScopeKey, NextSequence = 90 });

			result.NextNotApplied.Should().BeTrue();
			_saved.Pattern.Should().Be("FD{YYYY}-{SEQ}");
			_sequences.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_refused_pattern_saves_nothing()
		{
			var result = await _service.SaveAsync(Dept, "admin", new CallNumberingUpdate { Pattern = "{MM}-{SEQ}", SequenceWidth = 4, NextSequence = 90 });

			result.PatternRejected.Should().BeTrue();
			_saved.Pattern.Should().BeNull();
			_sequences.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Renumbering_a_year_starts_each_sequence_at_its_raised_starting_point()
		{
			_saved = new CallNumberingConfig { Pattern = "{YYYY}-{SEQ}", SequenceWidth = 3 };
			Issued("x", new DateTime(2026, 3, 1));
			Issued("y", new DateTime(2026, 1, 15));
			Issued("z", new DateTime(2025, 12, 30));
			await _sequences.RaiseFloorAsync(Dept, "2026-#", 40, "admin", DateTime.UtcNow);

			(await _service.RenumberCallsForYearAsync(Dept, 2026)).Should().BeTrue();

			_calls.Single(c => c.CallId == 2).Number.Should().Be("2026-040");
			_calls.Single(c => c.CallId == 1).Number.Should().Be("2026-041");
			_calls.Single(c => c.CallId == 3).Number.Should().Be("z", "calls outside the year are untouched");
			(await _service.AllocateCallNumberAsync(Dept, new DateTime(2026, 6, 1))).Should().Be("2026-042");
		}

		[Test]
		public async Task A_pattern_without_a_year_is_never_renumbered_by_year()
		{
			_saved = new CallNumberingConfig { Pattern = "{SEQ}", SequenceWidth = 6 };
			Issued("000005", new DateTime(2026, 3, 1));

			(await _service.RenumberCallsForYearAsync(Dept, 2026)).Should().BeFalse();
			_calls.Single().Number.Should().Be("000005");
		}

		#endregion

		/// <summary>In-memory CallNumberSequences with the repository's semantics, reading issued numbers from the test's calls.</summary>
		private sealed class FakeSequences : ICallNumberSequencesRepository
		{
			public Dictionary<string, CallNumberSequence> Rows { get; } = new Dictionary<string, CallNumberSequence>(StringComparer.Ordinal);
			public List<Call> Calls { get; set; }
			public int SeedScans { get; private set; }

			public Task<CallNumberSequence> GetSequenceAsync(int departmentId, string scopeKey) =>
				Task.FromResult(Rows.TryGetValue(scopeKey, out var row) ? row : null);

			public Task<int> TakeNextAsync(int departmentId, string scopeKey, int seed, CancellationToken cancellationToken = default)
			{
				if (!Rows.TryGetValue(scopeKey, out var row))
					Rows[scopeKey] = row = new CallNumberSequence { DepartmentId = departmentId, ScopeKey = scopeKey, LastSequence = Math.Max(0, seed) + 1 };
				else
					row.LastSequence++;
				row.ModifiedOn = DateTime.UtcNow;
				return Task.FromResult(row.LastSequence);
			}

			public Task RaiseFloorAsync(int departmentId, string scopeKey, int nextSequence, string userId, DateTime now, CancellationToken cancellationToken = default)
			{
				if (!Rows.TryGetValue(scopeKey, out var row))
					Rows[scopeKey] = row = new CallNumberSequence { DepartmentId = departmentId, ScopeKey = scopeKey, LastSequence = nextSequence - 1, ModifiedOn = now };
				row.LastSequence = Math.Max(row.LastSequence, nextSequence - 1);
				row.FloorSequence = Math.Max(row.FloorSequence, nextSequence);
				row.FloorSetByUserId = userId;
				row.FloorSetOn = now;
				return Task.CompletedTask;
			}

			public Task SetLastSequenceAsync(int departmentId, string scopeKey, int lastSequence, CancellationToken cancellationToken = default)
			{
				if (!Rows.TryGetValue(scopeKey, out var row))
					Rows[scopeKey] = row = new CallNumberSequence { DepartmentId = departmentId, ScopeKey = scopeKey, ModifiedOn = DateTime.UtcNow };
				row.LastSequence = lastSequence;
				return Task.CompletedTask;
			}

			public Task<int> GetHighestIssuedAsync(int departmentId, string numberPrefix, string numberSuffix, DateTime? fromUtc, DateTime? toUtc)
			{
				SeedScans++;
				var highest = 0;
				foreach (var call in Calls.Where(c => (!fromUtc.HasValue || c.LoggedOn >= fromUtc) && (!toUtc.HasValue || c.LoggedOn < toUtc)))
				{
					var n = call.Number ?? string.Empty;
					if (n.Length <= numberPrefix.Length + numberSuffix.Length || !n.StartsWith(numberPrefix, StringComparison.Ordinal) || !n.EndsWith(numberSuffix, StringComparison.Ordinal))
						continue;
					var mid = n.Substring(numberPrefix.Length, n.Length - numberPrefix.Length - numberSuffix.Length);
					if (mid.All(char.IsDigit) && int.TryParse(mid, out var value))
						highest = Math.Max(highest, value);
				}
				return Task.FromResult(highest);
			}
		}
	}
}
