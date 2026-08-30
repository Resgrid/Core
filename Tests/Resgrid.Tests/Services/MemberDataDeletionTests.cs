using System;
using System.Collections.Generic;
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
	/// Deleting an account has to reach the department-scoped tables. Since the identification
	/// number and addresses moved off the global UserProfiles row (ADP plan 5.1), scrubbing the
	/// profile and deleting the legacy Addresses rows no longer removes them — those rows are now
	/// the only copy.
	/// </summary>
	[TestFixture]
	public class MemberDataDeletionTests
	{
		private const int DeptId = 8;
		private const string UserId = "user-1";

		[Test]
		public async Task Deleting_a_member_removes_their_department_scoped_row()
		{
			var repo = new Mock<IDepartmentMemberSensitiveDataRepository>();
			var row = new DepartmentMemberSensitiveData
			{
				DepartmentMemberSensitiveDataId = 3,
				DepartmentId = DeptId,
				UserId = UserId,
				IdentificationNumber = "BADGE-1"
			};
			repo.Setup(x => x.GetByDepartmentAndUserAsync(DeptId, UserId)).ReturnsAsync(row);
			repo.Setup(x => x.DeleteAsync(row, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var service = new DepartmentMemberSensitiveDataService(repo.Object,
				new Lazy<IProtectedWriteService>(() => new Mock<IProtectedWriteService>().Object),
				new Lazy<IProtectedReadService>(() => new Mock<IProtectedReadService>().Object));

			var deleted = await service.DeleteForMemberAsync(DeptId, UserId);

			deleted.Should().BeTrue();
			repo.Verify(x => x.DeleteAsync(row, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Deleting_a_member_with_no_row_is_a_no_op()
		{
			var repo = new Mock<IDepartmentMemberSensitiveDataRepository>();
			repo.Setup(x => x.GetByDepartmentAndUserAsync(DeptId, UserId))
				.ReturnsAsync((DepartmentMemberSensitiveData)null);

			var service = new DepartmentMemberSensitiveDataService(repo.Object,
				new Lazy<IProtectedWriteService>(() => new Mock<IProtectedWriteService>().Object),
				new Lazy<IProtectedReadService>(() => new Mock<IProtectedReadService>().Object));

			(await service.DeleteForMemberAsync(DeptId, UserId)).Should().BeFalse();
			repo.Verify(x => x.DeleteAsync(It.IsAny<DepartmentMemberSensitiveData>(), It.IsAny<CancellationToken>()),
				Times.Never);
		}

		[Test]
		public async Task Emergency_contacts_are_hard_deleted_including_already_soft_deleted_rows()
		{
			// The per-contact delete is a soft delete, which keeps envelopes and residue counts
			// consistent. Account deletion is different: a soft delete would leave a third party's
			// name and phone number in the table. The bulk path goes straight to a DELETE rather
			// than through the IsDeleted-filtered getter, so soft-deleted rows go too.
			var repo = new Mock<IDepartmentMemberEmergencyContactRepository>();
			repo.Setup(x => x.DeleteAllByDepartmentAndUserAsync(DeptId, UserId)).ReturnsAsync(3);

			var service = new DepartmentMemberEmergencyContactService(repo.Object,
				new Lazy<IProtectedWriteService>(() => new Mock<IProtectedWriteService>().Object));

			(await service.DeleteAllForMemberAsync(DeptId, UserId)).Should().Be(3);

			repo.Verify(x => x.DeleteAllByDepartmentAndUserAsync(DeptId, UserId), Times.Once);
			repo.Verify(x => x.GetAllByDepartmentAndUserAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Bulk_emergency_contact_deletion_ignores_an_unusable_scope()
		{
			var repo = new Mock<IDepartmentMemberEmergencyContactRepository>();
			var service = new DepartmentMemberEmergencyContactService(repo.Object,
				new Lazy<IProtectedWriteService>(() => new Mock<IProtectedWriteService>().Object));

			(await service.DeleteAllForMemberAsync(0, UserId)).Should().Be(0);
			(await service.DeleteAllForMemberAsync(DeptId, "  ")).Should().Be(0);

			repo.Verify(x => x.DeleteAllByDepartmentAndUserAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}
	}
}
