using System;
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
	/// A member's emergency contacts are department-scoped and there may be several. The write net
	/// envelopes them for a protected department; deletes are scoped by department AND user so an id
	/// alone can never reach another member's row.
	/// </summary>
	[TestFixture]
	public class MemberEmergencyContactProtectionTests
	{
		private const int DeptId = 10;
		private const string UserId = "user-1";

		private Mock<IDepartmentMemberEmergencyContactRepository> _repo;
		private Mock<IProtectedWriteService> _protectedWriteService;
		private DepartmentMemberEmergencyContactService _service;

		[SetUp]
		public void SetUp()
		{
			_repo = new Mock<IDepartmentMemberEmergencyContactRepository>();
			_repo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentMemberEmergencyContact>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentMemberEmergencyContact c, CancellationToken _, bool __) => c);

			_protectedWriteService = new Mock<IProtectedWriteService>();
			_protectedWriteService.Setup(x => x.PrepareMemberEmergencyContactWriteAsync(It.IsAny<int>(),
					It.IsAny<DepartmentMemberEmergencyContact>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());

			_service = new DepartmentMemberEmergencyContactService(_repo.Object,
				new Lazy<IProtectedWriteService>(() => _protectedWriteService.Object));
		}

		private static DepartmentMemberEmergencyContact BuildContact(int id = 3) => new DepartmentMemberEmergencyContact
		{
			DepartmentMemberEmergencyContactId = id,
			DepartmentId = DeptId,
			UserId = UserId,
			Name = "Jamie Doe",
			Relationship = "Spouse",
			PhoneNumber = "555-0100"
		};

		[Test]
		public async Task Protected_department_repersists_the_enveloped_contact()
		{
			_protectedWriteService.Setup(x => x.PrepareMemberEmergencyContactWriteAsync(DeptId,
					It.IsAny<DepartmentMemberEmergencyContact>(), null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed(isProtected: true, changed: true));

			await _service.SaveAsync(BuildContact());

			_repo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentMemberEmergencyContact>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
				Times.Exactly(2), "initial save plus the re-save that persists the envelopes");
		}

		[Test]
		public async Task Blocked_protected_write_throws_rather_than_leaving_plaintext()
		{
			_protectedWriteService.Setup(x => x.PrepareMemberEmergencyContactWriteAsync(DeptId,
					It.IsAny<DepartmentMemberEmergencyContact>(), null, null, true, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Blocked("broker_unavailable"));

			Func<Task> act = async () => await _service.SaveAsync(BuildContact());

			await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*broker_unavailable*");
		}

		[Test]
		public async Task A_member_can_hold_several_contacts_for_one_department()
		{
			_repo.Setup(x => x.GetAllByDepartmentAndUserAsync(DeptId, UserId))
				.ReturnsAsync(new[] { BuildContact(1), BuildContact(2), BuildContact(3) });

			var contacts = await _service.GetAllForMemberAsync(DeptId, UserId);

			contacts.Should().HaveCount(3);
		}

		[Test]
		public async Task Delete_refuses_an_id_that_belongs_to_another_member()
		{
			// The repository lookup is already scoped to (department, user); an id outside that set
			// must not be deletable, or one member could remove another's next-of-kin details.
			_repo.Setup(x => x.GetAllByDepartmentAndUserAsync(DeptId, UserId))
				.ReturnsAsync(new[] { BuildContact(1) });

			var deleted = await _service.DeleteAsync(99, DeptId, UserId, UserId);

			deleted.Should().BeFalse();
			_repo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentMemberEmergencyContact>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task Delete_is_a_soft_delete_so_envelopes_survive()
		{
			var contact = BuildContact(1);
			_repo.Setup(x => x.GetAllByDepartmentAndUserAsync(DeptId, UserId)).ReturnsAsync(new[] { contact });

			var deleted = await _service.DeleteAsync(1, DeptId, UserId, "admin-1");

			deleted.Should().BeTrue();
			contact.IsDeleted.Should().BeTrue();
			contact.UpdatedByUserId.Should().Be("admin-1");
			contact.Name.Should().Be("Jamie Doe", "a soft delete must not need to decrypt anything");
		}
	}
}
