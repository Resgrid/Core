using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class UnitStatusAlertsServiceTests
	{
		private const int DepartmentId = 4;
		private const int UnitId = 12;
		private const int UnitStateId = 900;
		private const string UserId = "dispatcher-1";

		private Mock<IUnitStatusAlertAcknowledgementsRepository> _repository;
		private Mock<IUnitsService> _unitsService;
		private Mock<ICustomStateService> _customStateService;
		private Mock<IDepartmentSettingsService> _settingsService;
		private Mock<IEventAggregator> _eventAggregator;
		private List<UnitStatusAlertAcknowledgement> _inserted;
		private List<UnitStatusAlertAcknowledgement> _updated;
		private UnitStatusAlertsService _service;

		[SetUp]
		public void SetUp()
		{
			_repository = new Mock<IUnitStatusAlertAcknowledgementsRepository>();
			_unitsService = new Mock<IUnitsService>();
			_customStateService = new Mock<ICustomStateService>();
			_settingsService = new Mock<IDepartmentSettingsService>();
			_eventAggregator = new Mock<IEventAggregator>();
			_inserted = new List<UnitStatusAlertAcknowledgement>();
			_updated = new List<UnitStatusAlertAcknowledgement>();

			_unitsService.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId });
			// Dispatched seven minutes ago against a 4 minute warning / 10 minute alert.
			GivenCurrentState(UnitStateId, DateTime.UtcNow.AddMinutes(-7));
			_customStateService.Setup(x => x.GetCustomUnitStateAsync(It.IsAny<UnitState>())).ReturnsAsync(new CustomStateDetail { BaseType = (int)ActionBaseTypes.Dispatched });
			_settingsService.Setup(x => x.GetUnitStatusThresholdsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new UnitStatusThresholds
			{
				Thresholds = new List<UnitStatusThreshold> { new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Dispatched, WarnSeconds = 240, AlertSeconds = 600 } }
			});

			_repository.Setup(x => x.GetActiveForUnitStateAsync(DepartmentId, UnitId, UnitStateId)).ReturnsAsync(new List<UnitStatusAlertAcknowledgement>());
			_repository.Setup(x => x.InsertAsync(It.IsAny<UnitStatusAlertAcknowledgement>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback<UnitStatusAlertAcknowledgement, CancellationToken, bool>((a, _, _) => _inserted.Add(a))
				.ReturnsAsync((UnitStatusAlertAcknowledgement a, CancellationToken _, bool _) => a);
			_repository.Setup(x => x.UpdateAsync(It.IsAny<UnitStatusAlertAcknowledgement>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback<UnitStatusAlertAcknowledgement, CancellationToken, bool>((a, _, _) => _updated.Add(a))
				.ReturnsAsync((UnitStatusAlertAcknowledgement a, CancellationToken _, bool _) => a);

			_service = new UnitStatusAlertsService(_repository.Object, _unitsService.Object, _customStateService.Object, _settingsService.Object, _eventAggregator.Object);
		}

		private void GivenCurrentState(int unitStateId, DateTime timestampUtc) =>
			_unitsService.Setup(x => x.GetLastUnitStateByUnitIdAsync(UnitId)).ReturnsAsync(new UnitState { UnitStateId = unitStateId, UnitId = UnitId, Timestamp = timestampUtc });

		private Task<UnitStatusAlertAcknowledgementResult> Acknowledge(UnitStatusAlertLevels level = UnitStatusAlertLevels.Warn,
			UnitStatusAlertAcknowledgementModes mode = UnitStatusAlertAcknowledgementModes.Acknowledged, int muteMinutes = 0, string note = null, int unitStateId = UnitStateId) =>
			_service.AcknowledgeAsync(DepartmentId, UnitId, unitStateId, level, mode, muteMinutes, note, UserId);

		[Test]
		public async Task Acknowledging_an_overdue_unit_records_who_saw_it_and_the_note()
		{
			var result = await Acknowledge(note: "  MUG not departed, technical malfunction reported.  ");

			result.Success.Should().BeTrue();
			_inserted.Should().ContainSingle();
			var saved = _inserted.Single();
			saved.UnitStatusAlertAcknowledgementId.Should().NotBeNullOrWhiteSpace();
			saved.DepartmentId.Should().Be(DepartmentId);
			saved.UnitStateId.Should().Be(UnitStateId);
			saved.Level.Should().Be((int)UnitStatusAlertLevels.Warn);
			saved.Mode.Should().Be((int)UnitStatusAlertAcknowledgementModes.Acknowledged);
			saved.MutedUntil.Should().BeNull();
			saved.Note.Should().Be("MUG not departed, technical malfunction reported.");
			saved.AcknowledgedByUserId.Should().Be(UserId);
		}

		[Test]
		public async Task Acknowledging_tells_every_board_in_the_department()
		{
			await Acknowledge();

			_eventAggregator.Verify(x => x.SendMessageAsync(It.Is<UnitStatusAlertUpdatedEvent>(e => e.DepartmentId == DepartmentId && e.UnitId == UnitId)), Times.Once);
		}

		[Test]
		public async Task A_failed_push_does_not_undo_the_acknowledgement()
		{
			_eventAggregator.Setup(x => x.SendMessageAsync(It.IsAny<UnitStatusAlertUpdatedEvent>())).ThrowsAsync(new InvalidOperationException("broker down"));

			var result = await Acknowledge();

			result.Success.Should().BeTrue();
			_inserted.Should().ContainSingle();
		}

		[Test]
		public async Task A_blank_note_is_stored_as_none()
		{
			await Acknowledge(note: "   ");

			_inserted.Single().Note.Should().BeNull();
		}

		[Test]
		public async Task A_note_over_the_limit_is_refused()
		{
			var result = await Acknowledge(note: new string('x', UnitStatusAlertAcknowledgement.MaxNoteLength + 1));

			result.Success.Should().BeFalse();
			result.Error.Should().Be(UnitStatusAlertAcknowledgementResult.NoteTooLong);
			_inserted.Should().BeEmpty();
		}

		[Test]
		public async Task A_unit_in_another_department_is_not_found()
		{
			_unitsService.Setup(x => x.GetUnitByIdAsync(UnitId)).ReturnsAsync(new Unit { UnitId = UnitId, DepartmentId = DepartmentId + 1 });

			var result = await Acknowledge();

			result.Error.Should().Be(UnitStatusAlertAcknowledgementResult.NotFound);
			_inserted.Should().BeEmpty();
		}

		[Test]
		public async Task A_unit_that_has_moved_on_cannot_have_its_old_status_acknowledged()
		{
			GivenCurrentState(UnitStateId + 1, DateTime.UtcNow.AddMinutes(-7));

			var result = await Acknowledge();

			result.Error.Should().Be(UnitStatusAlertAcknowledgementResult.StatusChanged);
			_inserted.Should().BeEmpty();
		}

		[Test]
		public async Task A_unit_within_its_threshold_cannot_be_acknowledged()
		{
			GivenCurrentState(UnitStateId, DateTime.UtcNow.AddMinutes(-2));

			var result = await Acknowledge();

			result.Error.Should().Be(UnitStatusAlertAcknowledgementResult.NotOverdue);
			_inserted.Should().BeEmpty();
		}

		[Test]
		public async Task A_board_a_few_seconds_fast_can_still_acknowledge()
		{
			// The board's clock says four minutes; the server's says ten seconds short of it.
			GivenCurrentState(UnitStateId, DateTime.UtcNow.AddSeconds(-230));

			var result = await Acknowledge();

			result.Success.Should().BeTrue();
		}

		[Test]
		public async Task An_alert_cannot_be_acknowledged_before_it_fires()
		{
			// The unit is only at the warning level, so a request claiming the alert level is recorded as a warning.
			// When the alert does fire it comes back.
			var result = await Acknowledge(level: UnitStatusAlertLevels.Alert);

			result.Success.Should().BeTrue();
			_inserted.Single().Level.Should().Be((int)UnitStatusAlertLevels.Warn);
		}

		[Test]
		public async Task A_board_that_saw_the_warning_records_the_warning_even_after_the_alert_fired()
		{
			GivenCurrentState(UnitStateId, DateTime.UtcNow.AddMinutes(-11));

			var result = await Acknowledge(level: UnitStatusAlertLevels.Warn);

			result.Success.Should().BeTrue();
			_inserted.Single().Level.Should().Be((int)UnitStatusAlertLevels.Warn);
		}

		[Test]
		public async Task A_timed_mute_ends_after_the_chosen_minutes()
		{
			var before = DateTime.UtcNow;

			await Acknowledge(mode: UnitStatusAlertAcknowledgementModes.Muted, muteMinutes: 15);

			_inserted.Single().MutedUntil.Should().BeCloseTo(before.AddMinutes(15), TimeSpan.FromSeconds(5));
		}

		[Test]
		public async Task A_mute_without_minutes_lasts_until_the_status_changes()
		{
			await Acknowledge(mode: UnitStatusAlertAcknowledgementModes.Muted, muteMinutes: 0);

			_inserted.Single().MutedUntil.Should().BeNull();
		}

		[Test]
		public async Task A_mute_is_capped_at_a_day()
		{
			var before = DateTime.UtcNow;

			await Acknowledge(mode: UnitStatusAlertAcknowledgementModes.Muted, muteMinutes: 100000);

			_inserted.Single().MutedUntil.Should().BeCloseTo(before.AddMinutes(UnitStatusAlertAcknowledgement.MaxMuteMinutes), TimeSpan.FromSeconds(5));
		}

		[Test]
		public async Task Minutes_are_ignored_on_a_plain_acknowledgement()
		{
			await Acknowledge(mode: UnitStatusAlertAcknowledgementModes.Acknowledged, muteMinutes: 30);

			_inserted.Single().MutedUntil.Should().BeNull();
		}

		[Test]
		public async Task An_unknown_mode_or_level_is_refused()
		{
			(await Acknowledge(mode: (UnitStatusAlertAcknowledgementModes)7)).Error.Should().Be(UnitStatusAlertAcknowledgementResult.InvalidMode);
			(await Acknowledge(level: UnitStatusAlertLevels.None)).Error.Should().Be(UnitStatusAlertAcknowledgementResult.InvalidLevel);
			_inserted.Should().BeEmpty();
		}

		[Test]
		public async Task Acknowledging_again_replaces_the_earlier_acknowledgement()
		{
			var earlier = new UnitStatusAlertAcknowledgement { UnitStatusAlertAcknowledgementId = "earlier", DepartmentId = DepartmentId, UnitId = UnitId, UnitStateId = UnitStateId };
			_repository.Setup(x => x.GetActiveForUnitStateAsync(DepartmentId, UnitId, UnitStateId)).ReturnsAsync(new List<UnitStatusAlertAcknowledgement> { earlier });

			var result = await Acknowledge(note: "second crew member unavailable");

			result.Success.Should().BeTrue();
			_updated.Should().ContainSingle().Which.UnitStatusAlertAcknowledgementId.Should().Be("earlier");
			earlier.ClearedOn.Should().NotBeNull();
			earlier.ClearedByUserId.Should().Be(UserId);
			_inserted.Should().ContainSingle();
		}

		[Test]
		public async Task Losing_a_race_hands_back_the_winning_acknowledgement()
		{
			var winner = new UnitStatusAlertAcknowledgement { UnitStatusAlertAcknowledgementId = "winner", DepartmentId = DepartmentId, UnitId = UnitId, UnitStateId = UnitStateId };
			_repository.SetupSequence(x => x.GetActiveForUnitStateAsync(DepartmentId, UnitId, UnitStateId))
				.ReturnsAsync(new List<UnitStatusAlertAcknowledgement>())
				.ReturnsAsync(new List<UnitStatusAlertAcknowledgement> { winner });
			_repository.Setup(x => x.InsertAsync(It.IsAny<UnitStatusAlertAcknowledgement>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("unique index"));

			var result = await Acknowledge();

			result.Success.Should().BeFalse();
			result.Error.Should().Be(UnitStatusAlertAcknowledgementResult.Conflict);
			result.Acknowledgement.Should().BeSameAs(winner);
		}

		[Test]
		public async Task An_insert_failure_that_is_not_a_race_is_not_swallowed()
		{
			_repository.Setup(x => x.InsertAsync(It.IsAny<UnitStatusAlertAcknowledgement>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("database down"));

			Func<Task> act = () => Acknowledge();

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task Clearing_marks_the_acknowledgement_and_tells_the_boards()
		{
			var existing = new UnitStatusAlertAcknowledgement { UnitStatusAlertAcknowledgementId = "a1", DepartmentId = DepartmentId, UnitId = UnitId, UnitStateId = UnitStateId };
			_repository.Setup(x => x.GetByIdAsync("a1")).ReturnsAsync(existing);

			var result = await _service.ClearAsync(DepartmentId, "a1", UserId);

			result.Success.Should().BeTrue();
			existing.ClearedOn.Should().NotBeNull();
			existing.ClearedByUserId.Should().Be(UserId);
			_eventAggregator.Verify(x => x.SendMessageAsync(It.Is<UnitStatusAlertUpdatedEvent>(e => e.UnitId == UnitId)), Times.Once);
		}

		[Test]
		public async Task Clearing_twice_is_not_an_error()
		{
			var clearedOn = DateTime.UtcNow.AddMinutes(-1);
			var existing = new UnitStatusAlertAcknowledgement { UnitStatusAlertAcknowledgementId = "a1", DepartmentId = DepartmentId, UnitId = UnitId, ClearedOn = clearedOn, ClearedByUserId = "someone-else" };
			_repository.Setup(x => x.GetByIdAsync("a1")).ReturnsAsync(existing);

			var result = await _service.ClearAsync(DepartmentId, "a1", UserId);

			result.Success.Should().BeTrue();
			existing.ClearedOn.Should().Be(clearedOn);
			existing.ClearedByUserId.Should().Be("someone-else");
			_updated.Should().BeEmpty();
		}

		[Test]
		public async Task Another_departments_acknowledgement_cannot_be_cleared()
		{
			_repository.Setup(x => x.GetByIdAsync("a1")).ReturnsAsync(new UnitStatusAlertAcknowledgement { UnitStatusAlertAcknowledgementId = "a1", DepartmentId = DepartmentId + 1 });

			var result = await _service.ClearAsync(DepartmentId, "a1", UserId);

			result.Error.Should().Be(UnitStatusAlertAcknowledgementResult.NotFound);
			_updated.Should().BeEmpty();
		}

		[Test]
		public async Task Only_acknowledgements_for_current_statuses_are_read()
		{
			_unitsService.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitId = 1, UnitStateId = 501 },
				new UnitState { UnitId = 2, UnitStateId = 0 }, // never reported a status
				new UnitState { UnitId = 3, UnitStateId = 503 }
			});
			IEnumerable<int> requested = null;
			_repository.Setup(x => x.GetActiveForUnitStatesAsync(DepartmentId, It.IsAny<IEnumerable<int>>()))
				.Callback<int, IEnumerable<int>>((_, ids) => requested = ids.ToList())
				.ReturnsAsync(new List<UnitStatusAlertAcknowledgement> { new UnitStatusAlertAcknowledgement { UnitStateId = 501 } });

			var result = await _service.GetCurrentAcknowledgementsForDepartmentAsync(DepartmentId);

			requested.Should().BeEquivalentTo(new[] { 501, 503 });
			result.Should().ContainSingle();
		}

		[Test]
		public async Task A_department_with_no_status_records_reads_nothing()
		{
			_unitsService.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitState> { new UnitState { UnitId = 1 } });

			var result = await _service.GetCurrentAcknowledgementsForDepartmentAsync(DepartmentId);

			result.Should().BeEmpty();
			_repository.Verify(x => x.GetActiveForUnitStatesAsync(It.IsAny<int>(), It.IsAny<IEnumerable<int>>()), Times.Never);
		}
	}
}
