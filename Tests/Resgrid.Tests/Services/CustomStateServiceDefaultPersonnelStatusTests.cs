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
	/// The status a person shows with no status inside the auto-available hour. A department on its own staff statuses
	/// used to see everyone fall back to the built-in "Standing By" (black, not one of its options) the next morning; it now
	/// shows the set's own Available option. Departments on the built-in statuses keep Standing By.
	/// </summary>
	[TestFixture]
	public class CustomStateServiceDefaultPersonnelStatusTests
	{
		private const int CustomDepartmentId = 14856;
		private const int BuiltInDepartmentId = 2;
		private const int NoAvailableDepartmentId = 3;
		private const int AvailableOptionId = 19721;

		private CustomStateService _service;

		[SetUp]
		public void SetUp()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;

			var repository = new Mock<ICustomStateRepository>();
			repository.Setup(x => x.GetCustomStatesByDepartmentIdAsync(CustomDepartmentId)).ReturnsAsync(new List<CustomState>
			{
				Personnel(CustomDepartmentId, 4547,
					new CustomStateDetail { CustomStateDetailId = 19722, CustomStateId = 4547, ButtonText = "En Route", ButtonColor = "#f08c00", BaseType = (int)ActionBaseTypes.Enroute, Order = 0 },
					new CustomStateDetail { CustomStateDetailId = AvailableOptionId, CustomStateId = 4547, ButtonText = "Available", ButtonColor = "#2f9e44", BaseType = (int)ActionBaseTypes.Available, Order = 1 })
			});
			repository.Setup(x => x.GetCustomStatesByDepartmentIdAsync(BuiltInDepartmentId)).ReturnsAsync(new List<CustomState>());
			repository.Setup(x => x.GetCustomStatesByDepartmentIdAsync(NoAvailableDepartmentId)).ReturnsAsync(new List<CustomState>
			{
				Personnel(NoAvailableDepartmentId, 51,
					new CustomStateDetail { CustomStateDetailId = 5101, CustomStateId = 51, ButtonText = "Responding", ButtonColor = "#f08c00", BaseType = (int)ActionBaseTypes.Responding })
			});

			_service = new CustomStateService(repository.Object, Mock.Of<ICustomStateDetailRepository>(), Mock.Of<IEventAggregator>(), Mock.Of<ICacheProvider>());
		}

		private static CustomState Personnel(int departmentId, int id, params CustomStateDetail[] details) => new CustomState
		{
			CustomStateId = id,
			DepartmentId = departmentId,
			Type = (int)CustomStateTypes.Personnel,
			Name = "Staff statuses",
			Details = new List<CustomStateDetail>(details)
		};

		[Test]
		public async Task A_custom_staff_set_defaults_to_its_own_Available_option()
		{
			var status = await _service.GetDefaultPersonnelStatusAsync(CustomDepartmentId);

			status.Should().NotBeNull();
			status.CustomStateDetailId.Should().Be(AvailableOptionId);
			status.ButtonText.Should().Be("Available");
			status.ButtonClassToColor().Should().Be("#2f9e44");
		}

		[Test]
		public async Task Built_in_statuses_default_to_Standing_By()
		{
			var status = await _service.GetDefaultPersonnelStatusAsync(BuiltInDepartmentId);

			status.Should().NotBeNull();
			status.CustomStateDetailId.Should().Be((int)ActionTypes.StandingBy);
			status.ButtonText.Should().Be("Standing By");
			status.ButtonClassToColor().Should().Be("#777", "the same colour an explicit Standing By status reports");
		}

		[Test]
		public async Task A_custom_set_without_an_Available_option_falls_back_to_Standing_By()
		{
			var status = await _service.GetDefaultPersonnelStatusAsync(NoAvailableDepartmentId);

			status.Should().NotBeNull();
			status.ButtonText.Should().Be("Standing By");
		}
	}
}
