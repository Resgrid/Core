using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// A member's identification number is DEPARTMENT-issued (ADP plan 5.1). UserProfile is global
	/// to the user and shared across every department they belong to, so it can neither be encrypted
	/// with one department's key nor hold the different numbers different departments issue the same
	/// person. These pin that the department-scoped value always wins.
	/// </summary>
	[TestFixture]
	public class MemberIdentificationNumberScopeTests
	{
		private const int DeptId = 10;

		private Mock<IDepartmentMemberSensitiveDataRepository> _repo;
		private Mock<IProtectedReadService> _protectedReadService;
		private DepartmentMemberSensitiveDataService _service;

		[SetUp]
		public void SetUp()
		{
			_repo = new Mock<IDepartmentMemberSensitiveDataRepository>();
			_protectedReadService = new Mock<IProtectedReadService>();
			_protectedReadService.Setup(x => x.ResolveMemberSensitiveDataForReadAsync(It.IsAny<int>(),
					It.IsAny<IReadOnlyList<DepartmentMemberSensitiveData>>(), It.IsAny<string>(),
					It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult());

			_service = new DepartmentMemberSensitiveDataService(_repo.Object,
				new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()),
				new Lazy<IProtectedReadService>(() => _protectedReadService.Object));
		}

		[Test]
		public async Task The_departments_number_replaces_the_global_profile_value()
		{
			_repo.Setup(x => x.GetAllByDepartmentIdAsync(DeptId)).ReturnsAsync(new[]
			{
				new DepartmentMemberSensitiveData { DepartmentId = DeptId, UserId = "user-1", IdentificationNumber = "DEPT-A-77" }
			});

			// The profile carries a number issued by a DIFFERENT department; it must not leak here.
			var profile = new UserProfile { UserId = "user-1", IdentificationNumber = "OTHER-DEPT-12" };

			await _service.ApplyIdentificationNumbersAsync(DeptId, new[] { profile }, null, "actor");

			profile.IdentificationNumber.Should().Be("DEPT-A-77");
		}

		[Test]
		public async Task A_member_with_no_row_for_this_department_has_no_number()
		{
			_repo.Setup(x => x.GetAllByDepartmentIdAsync(DeptId))
				.ReturnsAsync(Array.Empty<DepartmentMemberSensitiveData>());

			var profile = new UserProfile { UserId = "user-1", IdentificationNumber = "OTHER-DEPT-12" };

			await _service.ApplyIdentificationNumbersAsync(DeptId, new[] { profile }, null, "actor");

			profile.IdentificationNumber.Should().BeNull(
				"the legacy global column must never answer for a department that issued no number");
		}

		[Test]
		public async Task Values_are_resolved_through_the_protected_pipeline_before_being_applied()
		{
			_repo.Setup(x => x.GetAllByDepartmentIdAsync(DeptId)).ReturnsAsync(new[]
			{
				new DepartmentMemberSensitiveData { DepartmentId = DeptId, UserId = "user-1", IdentificationNumber = "rgdp:1:1:id==" }
			});

			var profile = new UserProfile { UserId = "user-1" };

			await _service.ApplyIdentificationNumbersAsync(DeptId, new[] { profile }, "grant", "actor");

			_protectedReadService.Verify(x => x.ResolveMemberSensitiveDataForReadAsync(DeptId,
					It.IsAny<IReadOnlyList<DepartmentMemberSensitiveData>>(), "grant", "actor", It.IsAny<CancellationToken>()),
				Times.Once, "ciphertext must never reach a report or API DTO");
		}

		[Test]
		public async Task User_id_matching_is_case_insensitive()
		{
			_repo.Setup(x => x.GetAllByDepartmentIdAsync(DeptId)).ReturnsAsync(new[]
			{
				new DepartmentMemberSensitiveData { DepartmentId = DeptId, UserId = "USER-1", IdentificationNumber = "DEPT-A-77" }
			});

			var profile = new UserProfile { UserId = "user-1" };

			await _service.ApplyIdentificationNumbersAsync(DeptId, new[] { profile }, null, "actor");

			profile.IdentificationNumber.Should().Be("DEPT-A-77", "identity user ids vary in casing across stores");
		}

		[Test]
		public async Task An_empty_profile_list_does_no_work()
		{
			await _service.ApplyIdentificationNumbersAsync(DeptId, Array.Empty<UserProfile>(), null, "actor");
			await _service.ApplyIdentificationNumbersAsync(DeptId, null, null, "actor");

			_repo.Verify(x => x.GetAllByDepartmentIdAsync(It.IsAny<int>()), Times.Never,
				"a report with no personnel must not pay for a query or a broker round trip");
		}
	}
}
