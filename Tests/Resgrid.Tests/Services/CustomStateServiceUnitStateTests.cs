using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The meaning (base type) the API reports for a unit's current status is what the Big Board's status timers key on.
	/// Before the built-in values carried one, every built-in status read as 0 (Available): a unit put on built-in
	/// Responding by dispatch automation was never timed, and the Unit Alerts widget stayed empty (customer report 2026-10-16).
	/// </summary>
	[TestFixture]
	public class CustomStateServiceUnitStateTests
	{
		private const int DepartmentId = 1;
		private const int DispatchedOptionId = 4100;

		private CustomStateService _service;

		[SetUp]
		public void SetUp()
		{
			var repository = new Mock<ICustomStateRepository>();
			repository.Setup(x => x.GetCustomStatesByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<CustomState>
			{
				new CustomState
				{
					CustomStateId = 41,
					DepartmentId = DepartmentId,
					Type = (int)CustomStateTypes.Unit,
					Details = new List<CustomStateDetail>
					{
						new CustomStateDetail { CustomStateDetailId = DispatchedOptionId, CustomStateId = 41, ButtonText = "Uitgerukt", BaseType = (int)ActionBaseTypes.Dispatched }
					}
				}
			});

			_service = new CustomStateService(repository.Object, Mock.Of<ICustomStateDetailRepository>(), Mock.Of<IEventAggregator>(), Mock.Of<ICacheProvider>());
		}

		private static UnitState StateFor(int value) => new UnitState
		{
			UnitId = 7,
			State = value,
			Timestamp = DateTime.UtcNow,
			Unit = new Unit { UnitId = 7, DepartmentId = DepartmentId }
		};

		[TestCase(UnitStateTypes.Committed, ActionBaseTypes.Dispatched)]
		[TestCase(UnitStateTypes.Responding, ActionBaseTypes.Responding)]
		[TestCase(UnitStateTypes.Enroute, ActionBaseTypes.Enroute)]
		[TestCase(UnitStateTypes.OnScene, ActionBaseTypes.OnScene)]
		[TestCase(UnitStateTypes.Staging, ActionBaseTypes.Staging)]
		[TestCase(UnitStateTypes.Returning, ActionBaseTypes.Returning)]
		public async Task BuiltInUnitStatus_CarriesItsMeaning_SoStatusTimersCanApply(UnitStateTypes builtIn, ActionBaseTypes expected)
		{
			var detail = await _service.GetCustomUnitStateAsync(StateFor((int)builtIn));

			detail.Should().NotBeNull();
			detail.BaseType.Should().Be((int)expected);
		}

		[Test]
		public async Task CustomUnitStatus_ReportsTheOptionsOwnMeaning()
		{
			var detail = await _service.GetCustomUnitStateAsync(StateFor(DispatchedOptionId));

			detail.ButtonText.Should().Be("Uitgerukt");
			detail.BaseType.Should().Be((int)ActionBaseTypes.Dispatched);
		}

		[Test]
		public async Task UnitPutOnBuiltInRespondingByDispatch_TripsARespondingTimer()
		{
			var thresholds = new UnitStatusThresholds
			{
				Thresholds = new List<UnitStatusThreshold> { new UnitStatusThreshold { BaseType = (int)ActionBaseTypes.Responding, WarnSeconds = 240, AlertSeconds = 480 } }
			};
			var state = StateFor((int)UnitStateTypes.Responding);
			state.Timestamp = new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

			var detail = await _service.GetCustomUnitStateAsync(state);

			UnitStatusAlertEvaluator.Evaluate(thresholds, detail.BaseType, state.Timestamp, state.Timestamp.AddMinutes(9))
				.Should().Be(UnitStatusAlertLevels.Alert);
		}
	}
}
