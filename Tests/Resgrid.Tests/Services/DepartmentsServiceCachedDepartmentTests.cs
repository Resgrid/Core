using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// A zero-byte or truncated Redis payload deserializes into a blank-but-non-null Department (id 0, null
	/// ManagingUserId) rather than a miss. Billing stamps payment rows straight off this object and
	/// Payments.PurchasingUserId is NOT NULL, so a blank record reaching the caller shows up as a NULL-insert
	/// SqlException in the billing worker. GetDepartmentByIdAsync must reject anything that isn't the
	/// department that was asked for.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class DepartmentsServiceCachedDepartmentTests
	{
		private const int DepartmentId = 4212;

		private Mock<IDepartmentsRepository> _departmentsRepository;
		private Mock<ICacheProvider> _cacheProvider;
		private DepartmentsService _service;
		private bool _originalCacheEnabled;

		[SetUp]
		public void SetUp()
		{
			_originalCacheEnabled = SystemBehaviorConfig.CacheEnabled;
			SystemBehaviorConfig.CacheEnabled = true;

			_departmentsRepository = new Mock<IDepartmentsRepository>();
			_cacheProvider = new Mock<ICacheProvider>();

			_service = new DepartmentsService(
				_departmentsRepository.Object,
				Mock.Of<IDepartmentMembersRepository>(),
				Mock.Of<ISubscriptionsService>(),
				Mock.Of<IDepartmentCallEmailsRepository>(),
				Mock.Of<IDepartmentCallPruningRepository>(),
				_cacheProvider.Object,
				Mock.Of<IUsersService>(),
				Mock.Of<IDepartmentSettingsService>(),
				Mock.Of<IUserProfileService>(),
				Mock.Of<ILimitsService>(),
				Mock.Of<IEventAggregator>(),
				Mock.Of<IIdentityRepository>(),
				Mock.Of<IDepartmentCallPruningRepository>());
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.CacheEnabled = _originalCacheEnabled;
		}

		[Test]
		public async Task GetDepartmentByIdAsync_BlankCachedDepartment_IsDiscardedAndReReadFromTheDatabase()
		{
			// The cache hands back a default-constructed Department, exactly what protobuf-net produces from
			// an empty payload.
			_cacheProvider
				.Setup(x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<Department>>>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync(new Department());

			var real = new Department { DepartmentId = DepartmentId, Name = "Test Fire", ManagingUserId = "user-1" };
			_departmentsRepository.Setup(x => x.GetDepartmentWithMembersByIdAsync(DepartmentId)).ReturnsAsync(real);

			var result = await _service.GetDepartmentByIdAsync(DepartmentId, false);

			result.Should().BeSameAs(real);
			result.ManagingUserId.Should().Be("user-1");
			_cacheProvider.Verify(x => x.RemoveAsync(It.IsAny<string>()), Times.Once);
			_departmentsRepository.Verify(x => x.GetDepartmentWithMembersByIdAsync(DepartmentId), Times.Once);
		}

		[Test]
		public async Task GetDepartmentByIdAsync_CachedDepartmentForAnotherId_IsDiscardedAndReReadFromTheDatabase()
		{
			_cacheProvider
				.Setup(x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<Department>>>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync(new Department { DepartmentId = 99, ManagingUserId = "someone-else" });

			var real = new Department { DepartmentId = DepartmentId, Name = "Test Fire", ManagingUserId = "user-1" };
			_departmentsRepository.Setup(x => x.GetDepartmentWithMembersByIdAsync(DepartmentId)).ReturnsAsync(real);

			var result = await _service.GetDepartmentByIdAsync(DepartmentId, false);

			result.DepartmentId.Should().Be(DepartmentId);
			_cacheProvider.Verify(x => x.RemoveAsync(It.IsAny<string>()), Times.Once);
		}

		[Test]
		public async Task GetDepartmentByIdAsync_MatchingCachedDepartment_IsReturnedWithoutHittingTheDatabase()
		{
			var cached = new Department { DepartmentId = DepartmentId, Name = "Test Fire", ManagingUserId = "user-1" };
			_cacheProvider
				.Setup(x => x.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<Department>>>(), It.IsAny<TimeSpan>()))
				.ReturnsAsync(cached);

			var result = await _service.GetDepartmentByIdAsync(DepartmentId, false);

			result.Should().BeSameAs(cached);
			_cacheProvider.Verify(x => x.RemoveAsync(It.IsAny<string>()), Times.Never);
			_departmentsRepository.Verify(x => x.GetDepartmentWithMembersByIdAsync(It.IsAny<int>()), Times.Never);
		}
	}
}
