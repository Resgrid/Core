using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05 item 2.5 (service): the holder can never sign off their own certification record — verify, reinstate,
	/// or keep a sign-off through their own edit or renewal of what was verified. Managers editing someone else's record keep
	/// today's behaviour.
	/// </summary>
	[TestFixture]
	public class BusinessOpsCertificationServiceTests
	{
		private const int Dept = 21;
		private const string Holder = "holder-1";
		private const string Manager = "manager-1";

		private List<PersonnelCertification> _records;
		private List<DepartmentCertificationType> _types;
		private Mock<IProtectedGrantContext> _grant;
		private CertificationService _service;

		[SetUp]
		public void SetUp()
		{
			_types = new List<DepartmentCertificationType>
			{
				new DepartmentCertificationType { DepartmentCertificationTypeId = 1, DepartmentId = Dept, Type = "Skills Check", Code = "SKILLS", IsActive = true, RequiresVerification = true },
				new DepartmentCertificationType { DepartmentCertificationTypeId = 2, DepartmentId = Dept, Type = "ICS-100", Code = "ICS-100", IsActive = true }
			};
			_records = new List<PersonnelCertification>();

			var types = new Mock<IDepartmentCertificationTypeRepository>();
			types.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _types.FirstOrDefault(t => t.DepartmentCertificationTypeId == (int)id));
			var records = new Mock<IPersonnelCertificationRepository>();
			// Every read hands back a copy, as Dapper does: the service compares the stored row with the incoming one.
			records.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => Copy(_records.FirstOrDefault(c => c.PersonnelCertificationId == (int)id)));
			records.Setup(r => r.SaveOrUpdateAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((PersonnelCertification c, CancellationToken _, bool __) =>
			{
				if (c.PersonnelCertificationId == 0) c.PersonnelCertificationId = _records.Count + 100;
				_records.RemoveAll(x => x.PersonnelCertificationId == c.PersonnelCertificationId);
				_records.Add(Copy(c));
				return c;
			});
			var protectedWrite = new Mock<IProtectedWriteService>();
			protectedWrite.Setup(w => w.PrepareCertificationWriteAsync(It.IsAny<int>(), It.IsAny<PersonnelCertification>(), It.IsAny<PersonnelCertification>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(ProtectedWriteResult.Allowed());
			_grant = new Mock<IProtectedGrantContext>();

			_service = new CertificationService(types.Object, records.Object, new Lazy<IProtectedWriteService>(() => protectedWrite.Object),
				Mock.Of<IPersonnelRoleCertificationRequirementRepository>(), Mock.Of<IDepartmentCertificationSettingsRepository>(), Mock.Of<IPersonnelCertificationCreditsRepository>(),
				Mock.Of<IUnitCertificationRepository>(), Mock.Of<IEventAggregator>(), new Lazy<IPersonnelRolesService>(() => Mock.Of<IPersonnelRolesService>()),
				new Lazy<IUnitsService>(() => Mock.Of<IUnitsService>()), new Lazy<IUserProfileService>(() => Mock.Of<IUserProfileService>()),
				new Lazy<IDepartmentsService>(() => Mock.Of<IDepartmentsService>()), new Lazy<IDepartmentSettingsService>(() => Mock.Of<IDepartmentSettingsService>()),
				new Lazy<ICommunicationService>(() => Mock.Of<ICommunicationService>()), null, _grant.Object);
		}

		private static PersonnelCertification Copy(PersonnelCertification c) => c == null ? null : JsonConvert.DeserializeObject<PersonnelCertification>(JsonConvert.SerializeObject(c));

		private PersonnelCertification Seed(int typeId, int status, bool verified, DateTime? expires = null)
		{
			var record = new PersonnelCertification
			{
				PersonnelCertificationId = 500 + _records.Count, DepartmentId = Dept, UserId = Holder, DepartmentCertificationTypeId = typeId, Type = "t", Name = "Cert", Number = "A-1",
				Area = "North", Status = status, ExpiresOn = expires ?? DateTime.UtcNow.Date.AddYears(1), RecievedOn = DateTime.UtcNow.Date.AddYears(-1),
				VerifiedByUserId = verified ? Manager : null, VerifiedOn = verified ? DateTime.UtcNow.AddDays(-30) : (DateTime?)null
			};
			_records.Add(record);
			return Copy(record);
		}

		private PersonnelCertification Stored(int id) => _records.Single(r => r.PersonnelCertificationId == id);

		[Test]
		public async Task Verify_refuses_the_holder_and_admits_another_manager()
		{
			var record = Seed(1, (int)PersonnelCertificationStatuses.PendingVerification, verified: false);

			var self = () => _service.VerifyCertificationAsync(record.PersonnelCertificationId, Dept, Holder);
			await self.Should().ThrowAsync<InvalidOperationException>().WithMessage(CertificationService.SelfVerificationRefused);
			Stored(record.PersonnelCertificationId).Status.Should().Be((int)PersonnelCertificationStatuses.PendingVerification);

			var verified = await _service.VerifyCertificationAsync(record.PersonnelCertificationId, Dept, Manager);
			verified.Status.Should().Be((int)PersonnelCertificationStatuses.Active);
			verified.VerifiedByUserId.Should().Be(Manager);
		}

		[Test]
		public async Task Setting_ones_own_record_back_to_active_is_a_sign_off_and_is_refused()
		{
			var record = Seed(1, (int)PersonnelCertificationStatuses.Suspended, verified: true);

			var reinstate = () => _service.SetCertificationStatusAsync(record.PersonnelCertificationId, Dept, PersonnelCertificationStatuses.Active, null, Holder);
			await reinstate.Should().ThrowAsync<InvalidOperationException>().WithMessage(CertificationService.SelfVerificationRefused);

			(await _service.SetCertificationStatusAsync(record.PersonnelCertificationId, Dept, PersonnelCertificationStatuses.Active, null, Manager)).Status
				.Should().Be((int)PersonnelCertificationStatuses.Active, "another manager may reinstate it");
		}

		[Test]
		public async Task A_holder_pushing_the_expiry_of_a_verified_record_sends_it_back_for_verification()
		{
			var record = Seed(1, (int)PersonnelCertificationStatuses.Active, verified: true);
			_grant.SetupGet(g => g.UserId).Returns(Holder);

			record.ExpiresOn = new DateTime(2099, 12, 31);
			var saved = await _service.SaveCertificationAsync(record);

			saved.Status.Should().Be((int)PersonnelCertificationStatuses.PendingVerification, "the type requires sign-off and the holder changed what was signed off");
			saved.VerifiedByUserId.Should().BeNull();
			saved.VerifiedOn.Should().BeNull();
		}

		[Test]
		public async Task A_holder_changing_the_number_or_document_also_loses_the_sign_off_but_a_note_does_not()
		{
			var numbered = Seed(1, (int)PersonnelCertificationStatuses.Active, verified: true);
			var noted = Seed(1, (int)PersonnelCertificationStatuses.Active, verified: true);
			var filed = Seed(1, (int)PersonnelCertificationStatuses.Active, verified: true);
			_grant.SetupGet(g => g.UserId).Returns(Holder);

			numbered.Number = "B-2";
			(await _service.SaveCertificationAsync(numbered)).Status.Should().Be((int)PersonnelCertificationStatuses.PendingVerification);

			noted.Area = "South";
			noted.Number = ProtectedDataEnvelope.RedactionValue;
			var kept = await _service.SaveCertificationAsync(noted);
			kept.Status.Should().Be((int)PersonnelCertificationStatuses.Active, "an unrevealed number posts the placeholder, which is not a change");
			kept.VerifiedByUserId.Should().Be(Manager);

			filed.Data = new byte[] { 1, 2, 3 };
			(await _service.SaveCertificationAsync(filed)).VerifiedOn.Should().BeNull("a new document was not what the verifier saw");
		}

		[Test]
		public async Task A_type_without_verification_stays_active_but_loses_a_stale_stamp()
		{
			var record = Seed(2, (int)PersonnelCertificationStatuses.Active, verified: true);
			_grant.SetupGet(g => g.UserId).Returns(Holder);

			record.ExpiresOn = record.ExpiresOn.Value.AddYears(2);
			var saved = await _service.SaveCertificationAsync(record);

			saved.Status.Should().Be((int)PersonnelCertificationStatuses.Active, "a new record of this type would be Active without sign-off too");
			saved.VerifiedByUserId.Should().BeNull();
		}

		[Test]
		public async Task A_manager_editing_someone_elses_record_keeps_todays_behaviour()
		{
			var record = Seed(1, (int)PersonnelCertificationStatuses.Active, verified: true);
			_grant.SetupGet(g => g.UserId).Returns(Manager);

			record.ExpiresOn = record.ExpiresOn.Value.AddYears(1);
			var saved = await _service.SaveCertificationAsync(record);

			saved.Status.Should().Be((int)PersonnelCertificationStatuses.Active);
			saved.VerifiedByUserId.Should().Be(Manager);
		}

		[Test]
		public async Task A_workload_save_without_an_attended_user_is_never_treated_as_the_holder()
		{
			var record = Seed(1, (int)PersonnelCertificationStatuses.Active, verified: true);
			_grant.SetupGet(g => g.UserId).Returns((string)null);

			record.ExpiresOn = record.ExpiresOn.Value.AddYears(1);
			(await _service.SaveCertificationAsync(record)).VerifiedByUserId.Should().Be(Manager);
		}

		[Test]
		public async Task A_holder_renewing_a_current_record_needs_a_new_sign_off_while_a_manager_renewal_does_not()
		{
			var mine = Seed(1, (int)PersonnelCertificationStatuses.Active, verified: true);
			var theirs = Seed(1, (int)PersonnelCertificationStatuses.Active, verified: true);

			var renewed = await _service.RenewCertificationAsync(mine.PersonnelCertificationId, Dept, mine.ExpiresOn.Value.AddYears(2), null, Holder);
			renewed.Status.Should().Be((int)PersonnelCertificationStatuses.PendingVerification);
			renewed.VerifiedByUserId.Should().BeNull();

			var byManager = await _service.RenewCertificationAsync(theirs.PersonnelCertificationId, Dept, theirs.ExpiresOn.Value.AddYears(2), null, Manager);
			byManager.Status.Should().Be((int)PersonnelCertificationStatuses.Active);
			byManager.VerifiedByUserId.Should().Be(Manager);
		}
	}
}
