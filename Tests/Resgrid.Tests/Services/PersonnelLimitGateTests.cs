using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The personnel-limit check that gates AddPerson, ReactivateUser and AddExistingUser reads the plan counts fresh;
	/// the Personnel index keeps the 14-day cached answer for showing the Add button.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class PersonnelLimitGateTests
	{
		private const int Dept = 52;
		private bool _cacheEnabled;
		private Mock<ISubscriptionsService> _subscriptions;
		private Mock<ICacheProvider> _cache;
		private DepartmentPlanCount _counts;
		private LimitsService _service;

		[SetUp]
		public void SetUp()
		{
			_cacheEnabled = Resgrid.Config.SystemBehaviorConfig.CacheEnabled;
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = true;

			// No plan resolves to the free-plan personnel limit of 10.
			_counts = new DepartmentPlanCount { UsersCount = 9, UnitsCount = 0 };
			_subscriptions = new Mock<ISubscriptionsService>();
			_subscriptions.Setup(s => s.GetCurrentPlanForDepartmentAsync(Dept, It.IsAny<bool>())).ReturnsAsync((Plan)null);
			_subscriptions.Setup(s => s.GetPlanCountsForDepartmentAsync(Dept)).ReturnsAsync(() => _counts);

			// The cache still holds the answer from when the department had 2 people.
			_cache = new Mock<ICacheProvider>();
			_cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<DepartmentLimits>>>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync(new DepartmentLimits { PersonnelLimit = 10, PersonnelCount = 2 });

			_service = new LimitsService(_subscriptions.Object, _cache.Object);
		}

		[TearDown]
		public void TearDown() => Resgrid.Config.SystemBehaviorConfig.CacheEnabled = _cacheEnabled;

		[Test]
		public async Task A_gating_check_reads_fresh_counts_and_refuses_at_the_limit()
		{
			(await _service.CanDepartmentAddNewUserAsync(Dept, bypassCache: true)).Should().BeTrue("9 of 10 seats are used");

			_counts = new DepartmentPlanCount { UsersCount = 10, UnitsCount = 0 };
			(await _service.CanDepartmentAddNewUserAsync(Dept, bypassCache: true)).Should().BeFalse("all 10 seats are used, whatever the cache says");

			_cache.Verify(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<DepartmentLimits>>>(), It.IsAny<TimeSpan>()), Times.Never);
		}

		[Test]
		public async Task Counts_that_cannot_be_read_refuse_the_add()
		{
			_counts = null;

			(await _service.CanDepartmentAddNewUserAsync(Dept, bypassCache: true)).Should().BeFalse("missing billing data never hands out a seat");
		}

		[Test]
		public async Task The_display_check_keeps_using_the_cached_limits()
		{
			_counts = new DepartmentPlanCount { UsersCount = 10, UnitsCount = 0 };

			(await _service.CanDepartmentAddNewUserAsync(Dept)).Should().BeTrue("the cached answer (2 of 10) is what the index shows");
			_subscriptions.Verify(s => s.GetPlanCountsForDepartmentAsync(Dept), Times.Never);
		}
	}
}
