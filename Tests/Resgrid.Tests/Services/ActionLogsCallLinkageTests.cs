using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ActionLogsCallLinkageTests
	{
		private const int DepartmentId = 7;
		private const int CallId = 42;

		private Mock<IActionLogsRepository> _repository;
		private Mock<ICustomStateService> _customStates;
		private Mock<ICallStatusAttributionService> _attribution;
		private ActionLogsService _service;

		[SetUp]
		public void SetUp()
		{
			_repository = new Mock<IActionLogsRepository>();
			_customStates = new Mock<ICustomStateService>();
			_customStates.Setup(x => x.GetDefaultPersonStatuses()).Returns(new CustomStateService(null, null, null, null).GetDefaultPersonStatuses());
			_customStates.Setup(x => x.GetAllCustomStatesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CustomState>());
			_attribution = new Mock<ICallStatusAttributionService>();
			_attribution.Setup(x => x.GetInferredActionLogsForCallAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<ActionLog>());

			_service = new ActionLogsService(_repository.Object, new Mock<IUsersService>().Object, new Mock<IDepartmentMembersRepository>().Object,
				new Mock<IDepartmentGroupsService>().Object, new Mock<IDepartmentsService>().Object, new Mock<IDepartmentSettingsService>().Object,
				new Mock<IEventAggregator>().Object, new Mock<IGeoService>().Object, _customStates.Object, new Mock<ICacheProvider>().Object, _attribution.Object);
		}

		[Test]
		public async Task call_record_includes_responding_and_on_unit_statuses_and_releases()
		{
			_repository.Setup(x => x.GetActionLogsForCallAsync(DepartmentId, CallId)).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { ActionLogId = 1, ActionTypeId = (int)ActionTypes.Responding, DestinationId = CallId, DestinationType = (int)DestinationEntityTypes.Call },
				new ActionLog { ActionLogId = 2, ActionTypeId = (int)ActionTypes.OnUnit, DestinationId = CallId, DestinationType = (int)DestinationEntityTypes.Call },
				new ActionLog { ActionLogId = 3, ActionTypeId = (int)ActionTypes.OnScene, DestinationId = CallId, DestinationType = (int)DestinationEntityTypes.Call },
				new ActionLog { ActionLogId = 4, ActionTypeId = (int)ActionTypes.StandingBy, DestinationId = CallId, DestinationType = (int)DestinationEntityTypes.Call }
			});

			var logs = await _service.GetActionLogsForCallAsync(DepartmentId, CallId);

			logs.Select(x => x.ActionLogId).Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
		}

		[Test]
		public async Task call_record_drops_untyped_rows_whose_status_targets_stations()
		{
			_repository.Setup(x => x.GetActionLogsForCallAsync(DepartmentId, CallId)).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog { ActionLogId = 1, ActionTypeId = (int)ActionTypes.RespondingToScene, DestinationId = CallId },
				new ActionLog { ActionLogId = 2, ActionTypeId = (int)ActionTypes.RespondingToStation, DestinationId = CallId }
			});

			var logs = await _service.GetActionLogsForCallAsync(DepartmentId, CallId);

			logs.Select(x => x.ActionLogId).Should().BeEquivalentTo(new[] { 1 });
		}

		[Test]
		public async Task call_record_is_read_for_the_calls_department_only()
		{
			_repository.Setup(x => x.GetActionLogsForCallAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new List<ActionLog>());

			await _service.GetActionLogsForCallAsync(DepartmentId, CallId);

			_repository.Verify(x => x.GetActionLogsForCallAsync(DepartmentId, CallId), Times.Once);
		}
	}
}
