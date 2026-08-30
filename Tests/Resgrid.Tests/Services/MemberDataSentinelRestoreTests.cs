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
	/// The grantless-save data-loss path for department-scoped member data.
	///
	/// While protection is enforced, the profile page renders every cataloged value as the literal
	/// REDACTED placeholder — that is the point of it. An editor who changes one unrelated field and
	/// saves therefore posts the placeholder back for everything else, and the write net has to tell
	/// "the editor never saw this" apart from "the member cleared this". It can only do that against
	/// the STORED row: with no row to compare to, the sentinel policy clears the field instead, and
	/// the member's identification number, addresses and next-of-kin details are gone with no reveal
	/// able to bring them back.
	///
	/// So these tests pin the contract at the seam: the service must hand the stored row to the
	/// write net for every update.
	/// </summary>
	[TestFixture]
	public class MemberDataSentinelRestoreTests
	{
		private const int DeptId = 12;
		private const string UserId = "user-9";

		[TestFixture]
		public class SensitiveData
		{
			private Mock<IDepartmentMemberSensitiveDataRepository> _repo;
			private Mock<IProtectedWriteService> _protectedWriteService;
			private DepartmentMemberSensitiveDataService _service;
			private DepartmentMemberSensitiveData _stored;

			[SetUp]
			public void SetUp()
			{
				_stored = new DepartmentMemberSensitiveData
				{
					DepartmentMemberSensitiveDataId = 5,
					DepartmentId = DeptId,
					UserId = UserId,
					ProtectionId = "abc",
					IdentificationNumber = "D1234567",
					HomeAddress1 = "12 Station Road",
					HomeCity = "Springfield"
				};

				_repo = new Mock<IDepartmentMemberSensitiveDataRepository>();
				_repo.Setup(x => x.GetByDepartmentAndUserAsync(DeptId, UserId)).ReturnsAsync(() => _stored);
				_repo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentMemberSensitiveData>(),
						It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((DepartmentMemberSensitiveData d, CancellationToken _, bool __) => d);

				_protectedWriteService = new Mock<IProtectedWriteService>();

				// The real sentinel policy, so the test exercises the restore rather than a stub of it.
				_protectedWriteService.Setup(x => x.PrepareMemberSensitiveDataWriteAsync(It.IsAny<int>(),
						It.IsAny<DepartmentMemberSensitiveData>(), It.IsAny<DepartmentMemberSensitiveData>(),
						It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int _, DepartmentMemberSensitiveData incoming, DepartmentMemberSensitiveData existing,
						string __, string ___, bool ____, CancellationToken _____) =>
					{
						foreach (var accessor in ProtectedReadService.MemberSensitiveDataAccessors)
						{
							if (accessor.Value.Get(incoming) != ProtectedDataEnvelope.RedactionValue)
								continue;

							accessor.Value.Set(incoming, existing != null ? accessor.Value.Get(existing) : null);
						}

						return ProtectedWriteResult.Allowed(isProtected: true, changed: true);
					});

				_service = new DepartmentMemberSensitiveDataService(_repo.Object,
					new Lazy<IProtectedWriteService>(() => _protectedWriteService.Object),
					new Lazy<IProtectedReadService>(() => new Mock<IProtectedReadService>().Object));
			}

			[Test]
			public async Task A_grantless_save_keeps_the_stored_identification_number_and_address()
			{
				var posted = new DepartmentMemberSensitiveData
				{
					DepartmentMemberSensitiveDataId = 5,
					DepartmentId = DeptId,
					UserId = UserId,
					ProtectionId = "abc",
					IdentificationNumber = ProtectedDataEnvelope.RedactionValue,
					HomeAddress1 = ProtectedDataEnvelope.RedactionValue,
					HomeCity = "Shelbyville"        // the one field the editor actually changed
				};

				var saved = await _service.SaveAsync(posted);

				saved.IdentificationNumber.Should().Be("D1234567",
					"the editor was never shown it, so posting the placeholder back cannot mean 'clear it'");
				saved.HomeAddress1.Should().Be("12 Station Road");
				saved.HomeCity.Should().Be("Shelbyville", "a real edit still goes through");
			}

			[Test]
			public async Task The_stored_row_is_handed_to_the_write_net_on_every_update()
			{
				await _service.SaveAsync(new DepartmentMemberSensitiveData
				{
					DepartmentMemberSensitiveDataId = 5,
					DepartmentId = DeptId,
					UserId = UserId,
					ProtectionId = "abc"
				});

				_protectedWriteService.Verify(x => x.PrepareMemberSensitiveDataWriteAsync(DeptId,
					It.IsAny<DepartmentMemberSensitiveData>(),
					It.Is<DepartmentMemberSensitiveData>(e => e != null && e.DepartmentMemberSensitiveDataId == 5),
					It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
					Times.Once, "without the stored row the sentinel policy nulls instead of restoring");
			}

			[Test]
			public async Task A_brand_new_row_has_nothing_to_restore_from()
			{
				_stored = null;

				var saved = await _service.SaveAsync(new DepartmentMemberSensitiveData
				{
					DepartmentId = DeptId,
					UserId = UserId,
					IdentificationNumber = ProtectedDataEnvelope.RedactionValue
				});

				saved.IdentificationNumber.Should().BeNull(
					"there is no stored value behind the placeholder, so the literal word must not be kept");
			}
		}

		[TestFixture]
		public class EmergencyContacts
		{
			private Mock<IDepartmentMemberEmergencyContactRepository> _repo;
			private Mock<IProtectedWriteService> _protectedWriteService;
			private DepartmentMemberEmergencyContactService _service;
			private DepartmentMemberEmergencyContact _stored;

			[SetUp]
			public void SetUp()
			{
				_stored = new DepartmentMemberEmergencyContact
				{
					DepartmentMemberEmergencyContactId = 4,
					DepartmentId = DeptId,
					UserId = UserId,
					Name = "Jamie Doe",
					Relationship = "Spouse",
					PhoneNumber = "555-0100",
					Email = "jamie@example.com"
				};

				_repo = new Mock<IDepartmentMemberEmergencyContactRepository>();
				_repo.Setup(x => x.GetAllByDepartmentAndUserAsync(DeptId, UserId))
					.ReturnsAsync(() => _stored == null
						? Enumerable.Empty<DepartmentMemberEmergencyContact>()
						: new[] { _stored });
				_repo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<DepartmentMemberEmergencyContact>(),
						It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((DepartmentMemberEmergencyContact c, CancellationToken _, bool __) => c);

				_protectedWriteService = new Mock<IProtectedWriteService>();
				_protectedWriteService.Setup(x => x.PrepareMemberEmergencyContactWriteAsync(It.IsAny<int>(),
						It.IsAny<DepartmentMemberEmergencyContact>(), It.IsAny<DepartmentMemberEmergencyContact>(),
						It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int _, DepartmentMemberEmergencyContact incoming, DepartmentMemberEmergencyContact existing,
						string __, string ___, bool ____, CancellationToken _____) =>
					{
						foreach (var accessor in ProtectedReadService.MemberEmergencyContactAccessors)
						{
							if (accessor.Value.Get(incoming) != ProtectedDataEnvelope.RedactionValue)
								continue;

							accessor.Value.Set(incoming, existing != null ? accessor.Value.Get(existing) : null);
						}

						return ProtectedWriteResult.Allowed(isProtected: true, changed: true);
					});

				_service = new DepartmentMemberEmergencyContactService(_repo.Object,
					new Lazy<IProtectedWriteService>(() => _protectedWriteService.Object));
			}

			[Test]
			public async Task A_grantless_save_keeps_the_stored_next_of_kin_details()
			{
				var saved = await _service.SaveAsync(new DepartmentMemberEmergencyContact
				{
					DepartmentMemberEmergencyContactId = 4,
					DepartmentId = DeptId,
					UserId = UserId,
					Name = ProtectedDataEnvelope.RedactionValue,
					Relationship = ProtectedDataEnvelope.RedactionValue,
					PhoneNumber = "555-0199",       // the one field the editor actually changed
					Email = ProtectedDataEnvelope.RedactionValue
				});

				saved.Name.Should().Be("Jamie Doe");
				saved.Relationship.Should().Be("Spouse");
				saved.Email.Should().Be("jamie@example.com");
				saved.PhoneNumber.Should().Be("555-0199", "a real edit still goes through");
			}

			[Test]
			public async Task The_stored_row_is_looked_up_within_the_members_own_department()
			{
				await _service.SaveAsync(new DepartmentMemberEmergencyContact
				{
					DepartmentMemberEmergencyContactId = 4,
					DepartmentId = DeptId,
					UserId = UserId,
					Name = "Jamie Doe"
				});

				// Through the member-scoped accessor, never by id alone: an id from another member or
				// another department must not be able to supply the restore source.
				_repo.Verify(x => x.GetAllByDepartmentAndUserAsync(DeptId, UserId), Times.AtLeastOnce);
			}
		}
	}
}
