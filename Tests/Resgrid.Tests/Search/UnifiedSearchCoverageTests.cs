using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>The operations reference families reach the index only under their pages' claim and module switch, and each hit is re-checked.</summary>
	public partial class UnifiedSearchServiceTests
	{
		private UnifiedSearchService ServiceWithReferenceFamilies(Mock<IWorkLogsService> logs = null, Mock<IShiftsService> shifts = null)
		{
			var permissions = new Resgrid.Services.PermissionsService(_permissions.Object, Mock.Of<IUsersService>(), Mock.Of<IDepartmentGroupsService>());
			return new UnifiedSearchService(_global.Object, _actions.Object, _flags.Object, _auth.Object, _states.Object, _recordsSearch.Object,
				_recordsAuth.Object, _records.Object, _cutover.Object, _departments.Object, permissions, _groups.Object,
				_roles.Object, _calls.Object, _units.Object, _messages.Object, _documents.Object, _notes.Object,
				_contacts.Object, _protection.Object, _settings.Object, _projections.Object,
				logs: logs == null ? null : new Lazy<IWorkLogsService>(() => logs.Object),
				shifts: shifts == null ? null : new Lazy<IShiftsService>(() => shifts.Object));
		}

		[Test]
		public async Task Reference_families_follow_their_page_claims_and_module_switches()
		{
			await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Log:View", "Protocols:View", "Training:View", "Schedule:View", "Shift:View", "GenericGroup:View", "Record:View"));
			_lastQuery.EntityTypes.Should().Contain(new[] { SearchEntityTypes.Log, SearchEntityTypes.Protocol, SearchEntityTypes.Training, SearchEntityTypes.CalendarEvent, SearchEntityTypes.Shift, SearchEntityTypes.Group, SearchEntityTypes.Poi });
			_lastQuery.EntityTypes.Should().NotContain(SearchEntityTypes.Occupancy, "the occupancy module is closed without the Records prevention gate");

			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(7, true)).ReturnsAsync(new DepartmentModuleSettings { LogsDisabled = true, CalendarDisabled = true, MappingDisabled = true, ShiftsDisabled = true, TrainingDisabled = true });
			await _service.SearchAsync(new UnifiedSearchRequest { Text = "one" }, Principal("Log:View", "Protocols:View", "Training:View", "Schedule:View", "Shift:View", "GenericGroup:View"));
			_lastQuery.EntityTypes.Should().NotContain(new[] { SearchEntityTypes.Log, SearchEntityTypes.CalendarEvent, SearchEntityTypes.Poi, SearchEntityTypes.Shift, SearchEntityTypes.Training });
			_lastQuery.EntityTypes.Should().Contain(new[] { SearchEntityTypes.Protocol, SearchEntityTypes.Group }, "protocols and groups have no module switch");
		}

		[Test]
		public async Task A_log_hit_is_shown_only_while_the_log_is_in_the_callers_department()
		{
			var logs = new Mock<IWorkLogsService>();
			logs.Setup(l => l.GetWorkLogByIdAsync(1)).ReturnsAsync(new Log { LogId = 1, DepartmentId = 7 });
			logs.Setup(l => l.GetWorkLogByIdAsync(2)).ReturnsAsync(new Log { LogId = 2, DepartmentId = 99 });
			var service = ServiceWithReferenceFamilies(logs: logs);
			Answer(Hit(SearchEntityTypes.Log, "1"), Hit(SearchEntityTypes.Log, "2"), Hit(SearchEntityTypes.Log, "3"));

			var result = await service.SearchAsync(new UnifiedSearchRequest { Text = "ladder" }, Principal("Log:View"));

			result.Hits.Select(h => h.EntityId).Should().Equal("1");
			result.Total.Should().BeNull("two candidates were dropped");
		}

		[Test]
		public async Task A_reference_family_without_its_service_fails_closed()
		{
			Answer(Hit(SearchEntityTypes.Shift, "1"));

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "b shift" }, Principal("Shift:View"));

			result.Hits.Should().BeEmpty();
		}

		[Test]
		public async Task A_shift_hit_from_its_department_is_shown()
		{
			var shifts = new Mock<IShiftsService>();
			shifts.Setup(s => s.GetShiftByIdAsync(1)).ReturnsAsync(new Shift { ShiftId = 1, DepartmentId = 7, Name = "B Shift" });
			var service = ServiceWithReferenceFamilies(shifts: shifts);
			Answer(Hit(SearchEntityTypes.Shift, "1"));

			var result = await service.SearchAsync(new UnifiedSearchRequest { Text = "b shift" }, Principal("Shift:View"));

			result.Hits.Select(h => h.EntityId).Should().Equal("1");
			result.Total.Should().Be(1);
		}
	}
}
