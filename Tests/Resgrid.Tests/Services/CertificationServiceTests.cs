using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>Plan D10: catalog guards, record lifecycle, credits, unit scope guards, requirements, settings, the nightly passes and the role-membership gate.</summary>
	[TestFixture]
	public class CertificationServiceTests
	{
		private const int Dept = 3;
		private static readonly DateTime Today = new DateTime(2026, 9, 19);

		private List<DepartmentCertificationType> _types;
		private List<PersonnelCertification> _records;
		private List<UnitCertification> _unitRecords;
		private List<PersonnelRoleCertificationRequirement> _requirements;
		private List<PersonnelCertificationCredit> _credits;
		private DepartmentCertificationSettings _settings;
		private List<PersonnelRoleUser> _members;
		private readonly List<object> _published = new List<object>();
		private readonly List<(string UserId, string Message)> _notified = new List<(string, string)>();
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IProtectedWriteService> _protectedWrite;
		private CertificationService _service;

		private List<AuditEvent> Audits => _published.OfType<AuditEvent>().ToList();

		[SetUp]
		public void SetUp()
		{
			_published.Clear(); _notified.Clear();
			_types = new List<DepartmentCertificationType>
			{
				new DepartmentCertificationType { DepartmentCertificationTypeId = 1, DepartmentId = Dept, Type = "NREMT Paramedic", Code = "NREMT-P", AppliesTo = 0, IsActive = true, DefaultValidityMonths = 24, RenewalCreditHoursRequired = 60 },
				new DepartmentCertificationType { DepartmentCertificationTypeId = 2, DepartmentId = Dept, Type = "DOT Inspection", Code = "DOT-INSPECTION", AppliesTo = 1, IsActive = true, DefaultValidityMonths = 12 },
				new DepartmentCertificationType { DepartmentCertificationTypeId = 3, DepartmentId = Dept, Type = "Skills Check", Code = "SKILLS", AppliesTo = 0, IsActive = true, RequiresVerification = true, DefaultValidityMonths = 12 },
				new DepartmentCertificationType { DepartmentCertificationTypeId = 4, DepartmentId = Dept, Type = "ICS-100", Code = "ICS-100", AppliesTo = 0, IsActive = true, NeverExpires = true }
			};
			_records = new List<PersonnelCertification>();
			_unitRecords = new List<UnitCertification>();
			_requirements = new List<PersonnelRoleCertificationRequirement>();
			_credits = new List<PersonnelCertificationCredit>();
			_settings = null;
			_members = new List<PersonnelRoleUser>();

			var types = new Mock<IDepartmentCertificationTypeRepository>();
			types.Setup(r => r.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(() => _types.ToList());
			types.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _types.FirstOrDefault(t => t.DepartmentCertificationTypeId == (int)id));
			types.Setup(r => r.GetByCodeAsync(Dept, It.IsAny<string>())).ReturnsAsync((int _, string code) => _types.FirstOrDefault(t => !t.IsDeleted && string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase)));
			types.Setup(r => r.GetByCodesAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int _, IEnumerable<string> codes) => _types.Where(t => !t.IsDeleted && codes.Contains(t.Code, StringComparer.OrdinalIgnoreCase)).ToList());
			types.Setup(r => r.GetActiveForDepartmentAsync(Dept, It.IsAny<int?>())).ReturnsAsync((int _, int? scope) => _types.Where(t => !t.IsDeleted && t.IsActive && (!scope.HasValue || t.AppliesTo == scope.Value)).ToList());
			types.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DepartmentCertificationType>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((DepartmentCertificationType t, CancellationToken _, bool __) =>
			{
				if (t.DepartmentCertificationTypeId == 0) { t.DepartmentCertificationTypeId = _types.Max(x => x.DepartmentCertificationTypeId) + 1; _types.Add(t); }
				else { _types.RemoveAll(x => x.DepartmentCertificationTypeId == t.DepartmentCertificationTypeId); _types.Add(t); }
				return t;
			});

			var records = new Mock<IPersonnelCertificationRepository>();
			records.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _records.FirstOrDefault(c => c.PersonnelCertificationId == (int)id));
			records.Setup(r => r.GetForDepartmentAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int _, IEnumerable<string> users) => _records.Where(c => !c.IsDeleted && (users == null || users.Contains(c.UserId))).ToList());
			records.Setup(r => r.GetByTypeIdsAsync(Dept, It.IsAny<IEnumerable<int>>())).ReturnsAsync((int _, IEnumerable<int> ids) => _records.Where(c => !c.IsDeleted && c.DepartmentCertificationTypeId.HasValue && ids.Contains(c.DepartmentCertificationTypeId.Value)).ToList());
			records.Setup(r => r.GetExpiringAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync((int _, DateTime on) => _records.Where(c => !c.IsDeleted && c.ExpiresOn.HasValue && c.ExpiresOn <= on).ToList());
			records.Setup(r => r.CountByTypeIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _records.Count(c => !c.IsDeleted && c.DepartmentCertificationTypeId == id));
			records.Setup(r => r.GetDepartmentIdsWithTypedRecordsAsync()).ReturnsAsync(() => _records.Where(c => c.IsTyped).Select(c => c.DepartmentId).Distinct().ToList());
			records.Setup(r => r.SaveOrUpdateAsync(It.IsAny<PersonnelCertification>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((PersonnelCertification c, CancellationToken _, bool __) =>
			{
				if (c.PersonnelCertificationId == 0) c.PersonnelCertificationId = _records.Count == 0 ? 100 : _records.Max(x => x.PersonnelCertificationId) + 1;
				_records.RemoveAll(x => x.PersonnelCertificationId == c.PersonnelCertificationId); _records.Add(c); return c;
			});

			var unitRecords = new Mock<IUnitCertificationRepository>();
			unitRecords.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _unitRecords.FirstOrDefault(u => u.UnitCertificationId == (int)id));
			unitRecords.Setup(r => r.GetByIdWithDataAsync(It.IsAny<int>())).ReturnsAsync((int id) => _unitRecords.FirstOrDefault(u => u.UnitCertificationId == id));
			unitRecords.Setup(r => r.GetByUnitIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _unitRecords.Where(u => !u.IsDeleted && u.UnitId == id).ToList());
			unitRecords.Setup(r => r.GetForDepartmentAsync(Dept)).ReturnsAsync(() => _unitRecords.Where(u => !u.IsDeleted).ToList());
			unitRecords.Setup(r => r.GetExpiringAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync((int _, DateTime on) => _unitRecords.Where(u => !u.IsDeleted && u.ExpiresOn.HasValue && u.ExpiresOn <= on).ToList());
			unitRecords.Setup(r => r.CountByTypeIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _unitRecords.Count(u => !u.IsDeleted && u.DepartmentCertificationTypeId == id));
			unitRecords.Setup(r => r.GetDepartmentIdsAsync()).ReturnsAsync(() => _unitRecords.Select(u => u.DepartmentId).Distinct().ToList());
			unitRecords.Setup(r => r.SaveOrUpdateAsync(It.IsAny<UnitCertification>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((UnitCertification u, CancellationToken _, bool __) =>
			{
				if (u.UnitCertificationId == 0) u.UnitCertificationId = _unitRecords.Count == 0 ? 500 : _unitRecords.Max(x => x.UnitCertificationId) + 1;
				_unitRecords.RemoveAll(x => x.UnitCertificationId == u.UnitCertificationId); _unitRecords.Add(u); return u;
			});

			var requirements = new Mock<IPersonnelRoleCertificationRequirementRepository>();
			requirements.Setup(r => r.GetByRoleIdAsync(It.IsAny<int>())).ReturnsAsync((int role) => _requirements.Where(x => x.PersonnelRoleId == role).ToList());
			requirements.Setup(r => r.GetAllForDepartmentAsync(Dept)).ReturnsAsync(() => _requirements.ToList());
			requirements.Setup(r => r.CountByTypeIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _requirements.Count(x => x.DepartmentCertificationTypeId == id));
			requirements.Setup(r => r.GetDepartmentIdsAsync()).ReturnsAsync(() => _requirements.Select(x => x.DepartmentId).Distinct().ToList());
			requirements.Setup(r => r.SaveOrUpdateAsync(It.IsAny<PersonnelRoleCertificationRequirement>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((PersonnelRoleCertificationRequirement x, CancellationToken _, bool __) =>
			{
				if (x.PersonnelRoleCertificationRequirementId == 0) x.PersonnelRoleCertificationRequirementId = _requirements.Count == 0 ? 1 : _requirements.Max(r => r.PersonnelRoleCertificationRequirementId) + 1;
				_requirements.RemoveAll(r => r.PersonnelRoleCertificationRequirementId == x.PersonnelRoleCertificationRequirementId); _requirements.Add(x); return x;
			});
			requirements.Setup(r => r.DeleteAsync(It.IsAny<PersonnelRoleCertificationRequirement>(), It.IsAny<CancellationToken>())).ReturnsAsync((PersonnelRoleCertificationRequirement x, CancellationToken _) => _requirements.RemoveAll(r => r.PersonnelRoleCertificationRequirementId == x.PersonnelRoleCertificationRequirementId) > 0);

			var settings = new Mock<IDepartmentCertificationSettingsRepository>();
			settings.Setup(r => r.GetAsync(Dept)).ReturnsAsync(() => _settings);
			settings.Setup(r => r.SaveAsync(It.IsAny<DepartmentCertificationSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync((DepartmentCertificationSettings s, CancellationToken _) => _settings = s);

			var credits = new Mock<IPersonnelCertificationCreditsRepository>();
			credits.Setup(r => r.GetByCertificationIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _credits.Where(c => c.PersonnelCertificationId == id).ToList());
			credits.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _credits.FirstOrDefault(c => c.PersonnelCertificationCreditId == (int)id));
			credits.Setup(r => r.GetHourTotalsAsync(It.IsAny<IEnumerable<int>>())).ReturnsAsync((IEnumerable<int> ids) => (IReadOnlyDictionary<int, decimal>)_credits.Where(c => ids.Contains(c.PersonnelCertificationId)).GroupBy(c => c.PersonnelCertificationId).ToDictionary(g => g.Key, g => g.Sum(c => c.Hours)));
			credits.Setup(r => r.SaveOrUpdateAsync(It.IsAny<PersonnelCertificationCredit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((PersonnelCertificationCredit c, CancellationToken _, bool __) =>
			{
				if (c.PersonnelCertificationCreditId == 0) c.PersonnelCertificationCreditId = _credits.Count == 0 ? 1 : _credits.Max(x => x.PersonnelCertificationCreditId) + 1;
				_credits.RemoveAll(x => x.PersonnelCertificationCreditId == c.PersonnelCertificationCreditId); _credits.Add(c); return c;
			});
			credits.Setup(r => r.DeleteAsync(It.IsAny<PersonnelCertificationCredit>(), It.IsAny<CancellationToken>())).ReturnsAsync((PersonnelCertificationCredit c, CancellationToken _) => _credits.RemoveAll(x => x.PersonnelCertificationCreditId == c.PersonnelCertificationCreditId) > 0);

			var aggregator = new Mock<IEventAggregator>();
			aggregator.Setup(a => a.SendMessage(It.IsAny<object>())).Callback((object m) => _published.Add(m));
			aggregator.Setup(a => a.SendMessage<AuditEvent>(It.IsAny<AuditEvent>())).Callback((AuditEvent m) => _published.Add(m));

			_roles = new Mock<IPersonnelRolesService>();
			_roles.Setup(r => r.GetRoleByIdAsync(12)).ReturnsAsync(new PersonnelRole { PersonnelRoleId = 12, DepartmentId = Dept, Name = "Paramedic" });
			_roles.Setup(r => r.GetAllMembersOfRoleAsync(12)).ReturnsAsync(() => _members.ToList());
			_roles.Setup(r => r.DeleteRoleUsersAsync(It.IsAny<List<PersonnelRoleUser>>(), It.IsAny<CancellationToken>())).ReturnsAsync((List<PersonnelRoleUser> users, CancellationToken _) => { _members.RemoveAll(m => users.Any(u => u.PersonnelRoleUserId == m.PersonnelRoleUserId)); return true; });

			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetUnitByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => id == 7 ? new Unit { UnitId = 7, DepartmentId = Dept, Name = "Engine 1" } : null);
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool _) => new UserProfile { UserId = id, FirstName = "Member", LastName = id });
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Test" });
			departments.Setup(d => d.GetAllAdminsForDepartmentAsync(Dept)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { UserId = "admin-1" } });
			var departmentSettings = new Mock<IDepartmentSettingsService>();
			departmentSettings.Setup(d => d.GetTextToCallNumberForDepartmentAsync(Dept)).ReturnsAsync("+15555550100");
			var communication = new Mock<ICommunicationService>();
			communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), Dept, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()))
				.ReturnsAsync((string user, int _, string message, string __, Department ___, string ____, UserProfile _____, bool ______) => { _notified.Add((user, message)); return true; });

			_protectedWrite = new Mock<IProtectedWriteService>();
			_protectedWrite.Setup(p => p.PrepareCertificationWriteAsync(It.IsAny<int>(), It.IsAny<PersonnelCertification>(), It.IsAny<PersonnelCertification>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true, Changed = false });
			_protectedWrite.Setup(p => p.PrepareRecordsEntityWriteAsync(It.IsAny<int>(), It.IsAny<UnitCertification>(), It.IsAny<UnitCertification>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, (Func<UnitCertification, string>, Action<UnitCertification, string>)>>(), It.IsAny<Action>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true, Changed = false });
			_protectedWrite.Setup(p => p.PrepareRecordsEntityWriteAsync(It.IsAny<int>(), It.IsAny<PersonnelCertificationCredit>(), It.IsAny<PersonnelCertificationCredit>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, (Func<PersonnelCertificationCredit, string>, Action<PersonnelCertificationCredit, string>)>>(), It.IsAny<Action>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedWriteResult { Success = true, Changed = false });

			_service = new CertificationService(types.Object, records.Object, new Lazy<IProtectedWriteService>(() => _protectedWrite.Object), requirements.Object, settings.Object, credits.Object, unitRecords.Object,
				aggregator.Object, new Lazy<IPersonnelRolesService>(() => _roles.Object), new Lazy<IUnitsService>(() => units.Object), new Lazy<IUserProfileService>(() => profiles.Object),
				new Lazy<IDepartmentsService>(() => departments.Object), new Lazy<IDepartmentSettingsService>(() => departmentSettings.Object), new Lazy<ICommunicationService>(() => communication.Object));
		}

		private PersonnelCertification AddRecord(int typeId, DateTime? expires, string user = "u1", PersonnelCertificationStatuses status = PersonnelCertificationStatuses.Active)
		{
			var record = new PersonnelCertification { PersonnelCertificationId = _records.Count + 100, DepartmentId = Dept, UserId = user, Name = "Card", DepartmentCertificationTypeId = typeId, ExpiresOn = expires, Status = (int)status };
			_records.Add(record); return record;
		}

		#region Types

		[Test]
		public async Task Types_derive_codes_refuse_duplicates_and_lock_code_and_scope_once_referenced()
		{
			var saved = await _service.SaveCertificationTypeAsync(new DepartmentCertificationType { DepartmentId = Dept, Type = "Fire Officer I" }, "admin");
			saved.Code.Should().Be("FIRE-OFFICER-I"); saved.AddedByUserId.Should().Be("admin"); saved.IsActive.Should().BeTrue();
			Audits.Last().Type.Should().Be(AuditLogTypes.CertificationTypeAdded);

			(await FluentActions.Awaiting(() => _service.SaveCertificationTypeAsync(new DepartmentCertificationType { DepartmentId = Dept, Type = "Other", Code = "nremt-p" }, "admin")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_type_code_taken");
			(await FluentActions.Awaiting(() => _service.SaveCertificationTypeAsync(new DepartmentCertificationType { DepartmentId = Dept, Type = "  " }, "admin")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_type_name_required");

			_requirements.Add(new PersonnelRoleCertificationRequirement { PersonnelRoleCertificationRequirementId = 1, PersonnelRoleId = 12, DepartmentId = Dept, DepartmentCertificationTypeId = 1, IsMandatory = true });
			var paramedic = _types.First(t => t.DepartmentCertificationTypeId == 1);
			(await FluentActions.Awaiting(() => _service.SaveCertificationTypeAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = 1, DepartmentId = Dept, Type = paramedic.Type, Code = "P-NEW" }, "admin")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_type_code_locked");
			(await FluentActions.Awaiting(() => _service.DeleteCertificationTypeByIdAsync(1)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_type_in_use");

			AddRecord(3, Today.AddYears(1));
			(await FluentActions.Awaiting(() => _service.SaveCertificationTypeAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = 3, DepartmentId = Dept, Type = "Skills Check", Code = "SKILLS", AppliesTo = 1 }, "admin")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_type_scope_locked");

			var unit = await _service.SaveCertificationTypeAsync(new DepartmentCertificationType { DepartmentId = Dept, Type = "Pump Test", AppliesTo = 1, RequiresVerification = true, RenewalCreditHoursRequired = 8 }, "admin");
			unit.RequiresVerification.Should().BeFalse("units are not verified"); unit.RenewalCreditHoursRequired.Should().BeNull("units earn no credits");

			(await _service.DeleteCertificationTypeByIdAsync(4)).Should().BeTrue();
			_types.Single(t => t.DepartmentCertificationTypeId == 4).IsDeleted.Should().BeTrue("soft delete");
			(await _service.GetActiveCertificationTypesAsync(Dept, CertificationAppliesTo.Person)).Select(t => t.Code).Should().NotContain("ICS-100");
			(await _service.DoesCertificationTypeAlreadyExistAsync(Dept, "nremt p")).Should().BeTrue("code match on the derived code");
		}

		[Test]
		public async Task Templates_project_into_department_types_and_duplicate_codes_are_refused()
		{
			var added = await _service.CreateCertificationTypeFromTemplateAsync(Dept, "cdl-a", "admin");
			added.Code.Should().Be("CDL-A"); added.DefaultValidityMonths.Should().Be(48);
			(await FluentActions.Awaiting(() => _service.CreateCertificationTypeFromTemplateAsync(Dept, "cdl-a", "admin")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_type_code_taken");
			(await FluentActions.Awaiting(() => _service.CreateCertificationTypeFromTemplateAsync(Dept, "nope", "admin")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_template_not_found");
		}

		#endregion

		#region Records

		[Test]
		public async Task Saving_a_typed_record_links_the_catalog_name_starts_pending_when_verification_is_required_and_raises_added()
		{
			var saved = await _service.SaveCertificationAsync(new PersonnelCertification { DepartmentId = Dept, UserId = "u1", Name = "Skills", DepartmentCertificationTypeId = 3, ExpiresOn = Today.AddYears(1) });
			saved.Status.Should().Be((int)PersonnelCertificationStatuses.PendingVerification);
			saved.Type.Should().Be("Skills Check");
			_published.OfType<CertificationAddedEvent>().Single().TypeCode.Should().Be("SKILLS");
			Audits.Last().Type.Should().Be(AuditLogTypes.CertificationAdded);

			(await FluentActions.Awaiting(() => _service.SaveCertificationAsync(new PersonnelCertification { DepartmentId = Dept, UserId = "u1", Name = "X", DepartmentCertificationTypeId = 2 })).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_type_scope");
			(await FluentActions.Awaiting(() => _service.SaveCertificationAsync(new PersonnelCertification { DepartmentId = Dept, UserId = "u1", Name = "X", DepartmentCertificationTypeId = 99 })).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_type_not_found");
		}

		[Test]
		public async Task Verify_status_and_renew_follow_the_lifecycle_and_publish_once_each()
		{
			var pending = AddRecord(3, Today.AddYears(1), status: PersonnelCertificationStatuses.PendingVerification);
			var verified = await _service.VerifyCertificationAsync(pending.PersonnelCertificationId, Dept, "supervisor");
			verified.Status.Should().Be((int)PersonnelCertificationStatuses.Active); verified.VerifiedByUserId.Should().Be("supervisor"); verified.VerifiedOn.Should().NotBeNull();
			Audits.Last().Type.Should().Be(AuditLogTypes.CertificationVerified);
			(await FluentActions.Awaiting(() => _service.VerifyCertificationAsync(pending.PersonnelCertificationId, Dept, "supervisor")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_not_pending");

			var suspended = await _service.SetCertificationStatusAsync(pending.PersonnelCertificationId, Dept, PersonnelCertificationStatuses.Suspended, "investigation", "chief");
			suspended.StatusReason.Should().Be("investigation");
			var change = _published.OfType<CertificationStatusChangedEvent>().Last();
			change.OldStatus.Should().Be(0); change.NewStatus.Should().Be((int)PersonnelCertificationStatuses.Suspended); change.ChangedByUserId.Should().Be("chief");
			(await FluentActions.Awaiting(() => _service.SetCertificationStatusAsync(pending.PersonnelCertificationId, Dept, PersonnelCertificationStatuses.Expired, null, "chief")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_status_invalid");
			(await FluentActions.Awaiting(() => _service.SetCertificationStatusAsync(pending.PersonnelCertificationId, 99, PersonnelCertificationStatuses.Active, null, "chief")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_record_not_found");

			var expired = AddRecord(1, Today.AddDays(-2), status: PersonnelCertificationStatuses.Expired);
			(await FluentActions.Awaiting(() => _service.RenewCertificationAsync(expired.PersonnelCertificationId, Dept, null, null, "u1")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_expiry_required");
			var renewed = await _service.RenewCertificationAsync(expired.PersonnelCertificationId, Dept, Today.AddYears(2), "P-2028", "u1");
			renewed.Status.Should().Be((int)PersonnelCertificationStatuses.Active); renewed.Number.Should().Be("P-2028"); renewed.ExpiresOn.Should().Be(Today.AddYears(2));
			_published.OfType<CertificationRenewedEvent>().Single().PreviousExpiresOn.Should().Be(Today.AddDays(-2));
			(await FluentActions.Awaiting(() => _service.RenewCertificationAsync(renewed.PersonnelCertificationId, Dept, Today.AddYears(1), null, "u1")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_expiry_not_later");

			(await _service.SoftDeleteCertificationAsync(renewed.PersonnelCertificationId, Dept, "chief")).Should().BeTrue();
			_records.Single(r => r.PersonnelCertificationId == renewed.PersonnelCertificationId).IsDeleted.Should().BeTrue();
			Audits.Last().Type.Should().Be(AuditLogTypes.CertificationRemoved);
		}

		[Test]
		public async Task Credits_attach_to_person_records_only_and_roll_up()
		{
			var record = AddRecord(1, Today.AddYears(1));
			await _service.AddCertificationCreditAsync(new PersonnelCertificationCredit { PersonnelCertificationId = record.PersonnelCertificationId, DepartmentId = Dept, Hours = 12.5m, Category = "Cardiac" }, "u1");
			await _service.AddCertificationCreditAsync(new PersonnelCertificationCredit { PersonnelCertificationId = record.PersonnelCertificationId, DepartmentId = Dept, Hours = 4, Category = "Trauma" }, "u1");
			(await _service.GetCertificationCreditTotalsAsync(new[] { record.PersonnelCertificationId }))[record.PersonnelCertificationId].Should().Be(16.5m);
			Audits.Count(a => a.Type == AuditLogTypes.CertificationCreditAdded).Should().Be(2);
			(await FluentActions.Awaiting(() => _service.AddCertificationCreditAsync(new PersonnelCertificationCredit { PersonnelCertificationId = record.PersonnelCertificationId, DepartmentId = Dept, Hours = 0 }, "u1")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_credit_hours_invalid");
			var credit = _credits.First();
			(await _service.DeleteCertificationCreditAsync(credit.PersonnelCertificationCreditId, Dept, "chief")).Should().BeTrue();
			_credits.Should().HaveCount(1);
		}

		#endregion

		#region Unit records

		[Test]
		public async Task Unit_records_require_a_unit_scoped_type_and_a_unit_in_the_department()
		{
			(await FluentActions.Awaiting(() => _service.SaveUnitCertificationAsync(new UnitCertification { UnitId = 7, DepartmentId = Dept, DepartmentCertificationTypeId = 1 }, "chief")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_type_scope");
			(await FluentActions.Awaiting(() => _service.SaveUnitCertificationAsync(new UnitCertification { UnitId = 8, DepartmentId = Dept, DepartmentCertificationTypeId = 2 }, "chief")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_unit_not_found");

			var saved = await _service.SaveUnitCertificationAsync(new UnitCertification { UnitId = 7, DepartmentId = Dept, DepartmentCertificationTypeId = 2, Number = "INSP-1", ExpiresOn = Today.AddMonths(6), Data = new byte[] { 1, 2, 3 }, FileName = "report.pdf" }, "chief");
			saved.UnitCertificationId.Should().BeGreaterThan(0); saved.AddedByUserId.Should().Be("chief"); saved.FileSize.Should().Be(3);
			_unitRecords.Single().Data.Should().Equal(1, 2, 3);
			Audits.Last().Type.Should().Be(AuditLogTypes.UnitCertificationAdded);

			var edited = await _service.SaveUnitCertificationAsync(new UnitCertification { UnitCertificationId = saved.UnitCertificationId, UnitId = 7, DepartmentId = Dept, DepartmentCertificationTypeId = 2, Number = "INSP-2", ExpiresOn = Today.AddMonths(7) }, "chief");
			_unitRecords.Single().Data.Should().Equal(new byte[] { 1, 2, 3 }, "no new file keeps the stored one"); edited.EditedByUserId.Should().Be("chief");

			var suspended = await _service.SetUnitCertificationStatusAsync(saved.UnitCertificationId, Dept, UnitCertificationStatuses.Suspended, "failed re-test", "chief");
			suspended.Status.Should().Be((int)UnitCertificationStatuses.Suspended);
			(await _service.DeleteUnitCertificationAsync(saved.UnitCertificationId, Dept, "chief")).Should().BeTrue();
			(await _service.GetUnitCertificationsAsync(7)).Should().BeEmpty();
			(await FluentActions.Awaiting(() => _service.AddCertificationCreditAsync(new PersonnelCertificationCredit { PersonnelCertificationId = 1, DepartmentId = Dept, Hours = 1 }, "u1")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_record_not_found");
		}

		#endregion

		#region Requirements, settings, evaluation

		[Test]
		public async Task Requirements_replace_the_set_reject_unit_types_and_settings_validate()
		{
			var rows = await _service.SaveRoleRequirementsAsync(Dept, 12, new List<PersonnelRoleCertificationRequirement>
			{
				new PersonnelRoleCertificationRequirement { DepartmentCertificationTypeId = 1, IsMandatory = true },
				new PersonnelRoleCertificationRequirement { DepartmentCertificationTypeId = 3, IsMandatory = false, AnyOfGroup = 2 },
				new PersonnelRoleCertificationRequirement { DepartmentCertificationTypeId = 3, IsMandatory = true, AnyOfGroup = 1 }
			}, "admin");
			rows.Should().HaveCount(2, "one row per type, last wins"); rows.Single(r => r.DepartmentCertificationTypeId == 3).AnyOfGroup.Should().Be(1);
			Audits.Last().Type.Should().Be(AuditLogTypes.RoleCertificationRequirementChanged);
			(await FluentActions.Awaiting(() => _service.SaveRoleRequirementsAsync(Dept, 12, new List<PersonnelRoleCertificationRequirement> { new PersonnelRoleCertificationRequirement { DepartmentCertificationTypeId = 2 } }, "admin")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_type_scope");
			(await FluentActions.Awaiting(() => _service.SaveRoleRequirementsAsync(Dept, 13, new List<PersonnelRoleCertificationRequirement>(), "admin")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_role_not_found");
			(await _service.SaveRoleRequirementsAsync(Dept, 12, new List<PersonnelRoleCertificationRequirement> { new PersonnelRoleCertificationRequirement { DepartmentCertificationTypeId = 1, IsMandatory = true } }, "admin")).Should().HaveCount(1);
			_requirements.Should().HaveCount(1, "the dropped row was deleted");

			(await _service.GetCertificationSettingsAsync(Dept)).EnforcementMode.Should().Be((int)CertificationEnforcementModes.Off, "defaults when never saved");
			(await FluentActions.Awaiting(() => _service.SaveCertificationSettingsAsync(new DepartmentCertificationSettings { DepartmentId = Dept, EnforcementMode = 7 }, "admin")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_enforcement_invalid");
			(await FluentActions.Awaiting(() => _service.SaveCertificationSettingsAsync(new DepartmentCertificationSettings { DepartmentId = Dept, NotifyLeadDaysCsv = "900" }, "admin")).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("certifications_lead_days_invalid");
			var saved = await _service.SaveCertificationSettingsAsync(new DepartmentCertificationSettings { DepartmentId = Dept, EnforcementMode = 2, NotifyLeadDaysCsv = "7, 30,7,x", RoleRemovalGraceDays = 10 }, "admin");
			saved.NotifyLeadDaysCsv.Should().Be("30,7"); saved.UpdatedByUserId.Should().Be("admin");
			Audits.Last().Type.Should().Be(AuditLogTypes.DepartmentCertificationSettingsChanged);
		}

		[Test]
		public async Task Evaluation_reads_members_and_codes_and_the_dashboard_pairs_subjects_with_types()
		{
			_requirements.Add(new PersonnelRoleCertificationRequirement { PersonnelRoleCertificationRequirementId = 1, PersonnelRoleId = 12, DepartmentId = Dept, DepartmentCertificationTypeId = 1, IsMandatory = true, AddedOn = Today.AddYears(-1) });
			_members.AddRange(new[] { new PersonnelRoleUser { PersonnelRoleUserId = 1, PersonnelRoleId = 12, DepartmentId = Dept, UserId = "u1" }, new PersonnelRoleUser { PersonnelRoleUserId = 2, PersonnelRoleId = 12, DepartmentId = Dept, UserId = "u2" } });
			AddRecord(1, Today.AddYears(1), "u1");
			AddRecord(1, Today.AddDays(-40), "u2");
			var evaluations = await _service.EvaluateRoleRequirementsAsync(Dept, 12, Today);
			evaluations.Single(e => e.UserId == "u1").Qualified.Should().BeTrue();
			evaluations.Single(e => e.UserId == "u2").Qualified.Should().BeFalse();
			(await _service.GetQualifiedPersonnelForRoleAsync(Dept, 12)).Should().Equal("u1");
			(await _service.GetUsersWithValidCertificationAsync(Dept, new[] { "NREMT-P" })).Should().Equal("u1");
			(await _service.GetUsersWithValidCertificationAsync(Dept, new[] { "NREMT-P", "SKILLS" })).Should().BeEmpty("all-of needs every code");
			(await _service.GetUsersWithValidCertificationAsync(Dept, new[] { "NREMT-P", "SKILLS" }, allOf: false)).Should().Equal("u1");

			_unitRecords.Add(new UnitCertification { UnitCertificationId = 500, UnitId = 7, DepartmentId = Dept, DepartmentCertificationTypeId = 2, ExpiresOn = Today.AddDays(10), Status = 0 });
			var dashboard = await _service.GetExpiryDashboardAsync(Dept, Today);
			dashboard.PersonCells.Should().HaveCount(2); dashboard.UnitCells.Single().SubjectName.Should().Be("Engine 1"); dashboard.UnitCells.Single().DaysUntilExpiry.Should().Be(10);
			dashboard.ExpiredCount.Should().Be(1); dashboard.ExpiringCount.Should().Be(1);
			dashboard.PersonCells.Single(c => c.SubjectId == "u2").SubjectName.Should().Be("Member u2");
		}

		#endregion

		#region Sweep

		[Test]
		public async Task Sweep_expires_notifies_on_lead_days_handles_units_and_is_idempotent_per_day()
		{
			_settings = new DepartmentCertificationSettings { DepartmentId = Dept, NotifyLeadDaysCsv = "30,7", NotifyCertificationHolder = true, SendAdminDigest = true };
			var lapsed = AddRecord(1, Today.AddDays(-1), "u1");
			var soon = AddRecord(1, Today.AddDays(7), "u2");
			var later = AddRecord(1, Today.AddDays(8), "u3");
			var forever = AddRecord(4, Today.AddDays(-100), "u4");
			_unitRecords.Add(new UnitCertification { UnitCertificationId = 500, UnitId = 7, DepartmentId = Dept, DepartmentCertificationTypeId = 2, ExpiresOn = Today.AddDays(-1), Status = 0 });
			_unitRecords.Add(new UnitCertification { UnitCertificationId = 501, UnitId = 7, DepartmentId = Dept, DepartmentCertificationTypeId = 2, ExpiresOn = Today.AddDays(30), Status = 0 });

			var result = await _service.RunExpirySweepAsync(Dept, Today);
			result.Expired.Should().Be(1); result.ExpiringNotified.Should().Be(1); result.UnitsExpired.Should().Be(1); result.UnitsExpiringNotified.Should().Be(1); result.DigestSent.Should().BeTrue();
			lapsed.Status.Should().Be((int)PersonnelCertificationStatuses.Expired); lapsed.StatusChangedByUserId.Should().Be(CertificationService.SystemUserId);
			later.Status.Should().Be((int)PersonnelCertificationStatuses.Active); forever.Status.Should().Be((int)PersonnelCertificationStatuses.Active, "never-expiring types are skipped");
			_published.OfType<CertificationExpiredEvent>().Single().Certification.PersonnelCertificationId.Should().Be(lapsed.PersonnelCertificationId);
			_published.OfType<CertificationExpiringEvent>().Single().DaysUntilExpiry.Should().Be(7);
			_published.OfType<UnitCertificationExpiredEvent>().Single().UnitName.Should().Be("Engine 1");
			_published.OfType<UnitCertificationExpiringEvent>().Single().DaysUntilExpiry.Should().Be(30);
			_unitRecords.Single(u => u.UnitCertificationId == 500).Status.Should().Be((int)UnitCertificationStatuses.Expired);
			_notified.Should().Contain(n => n.UserId == "u2" && n.Message.Contains("7 days"));
			_notified.Should().Contain(n => n.UserId == "admin-1" && n.Message.Contains("2 expired, 2 expiring"));
			_notified.Should().NotContain(n => n.UserId == "u1", "an expired record raises the department event; the holder direct notice is for lead days");

			_published.Clear(); _notified.Clear();
			var again = await _service.RunExpirySweepAsync(Dept, Today);
			again.Expired.Should().Be(0); again.UnitsExpired.Should().Be(0); again.ExpiringNotified.Should().Be(1, "the same local day repeats the lead-day match; a new day will not");
			(await _service.RunExpirySweepAsync(Dept, Today.AddDays(1))).ExpiringNotified.Should().Be(1, "only the record due in 8 days reaches its 7-day lead tomorrow");
		}

		[Test]
		public async Task Enforcement_removes_only_under_enforce_and_only_after_grace_and_audits_the_removal()
		{
			_requirements.Add(new PersonnelRoleCertificationRequirement { PersonnelRoleCertificationRequirementId = 1, PersonnelRoleId = 12, DepartmentId = Dept, DepartmentCertificationTypeId = 1, IsMandatory = true, AddedOn = Today.AddYears(-1) });
			_members.Add(new PersonnelRoleUser { PersonnelRoleUserId = 1, PersonnelRoleId = 12, DepartmentId = Dept, UserId = "u1" });
			_members.Add(new PersonnelRoleUser { PersonnelRoleUserId = 2, PersonnelRoleId = 12, DepartmentId = Dept, UserId = "u2" });
			AddRecord(1, Today.AddDays(-31), "u1", PersonnelCertificationStatuses.Expired);
			AddRecord(1, Today.AddDays(-5), "u2", PersonnelCertificationStatuses.Expired);

			_settings = new DepartmentCertificationSettings { DepartmentId = Dept, EnforcementMode = (int)CertificationEnforcementModes.Off, RoleRemovalGraceDays = 30, SendAdminDigest = false };
			(await _service.RunExpirySweepAsync(Dept, Today)).Removed.Should().Be(0); _members.Should().HaveCount(2);

			_settings.EnforcementMode = (int)CertificationEnforcementModes.WarnOnly;
			var warn = await _service.RunExpirySweepAsync(Dept, Today);
			warn.Removed.Should().Be(0); warn.InGrace.Should().Be(2); _members.Should().HaveCount(2);

			_settings.EnforcementMode = (int)CertificationEnforcementModes.Enforce;
			var enforce = await _service.RunExpirySweepAsync(Dept, Today);
			enforce.Removed.Should().Be(1, "u1 lapsed 31 days ago (grace 30 ended yesterday); u2 is inside grace"); enforce.InGrace.Should().Be(1);
			_members.Select(m => m.UserId).Should().Equal("u2");
			var removal = _published.OfType<CertificationRoleRemovedEvent>().Single();
			removal.UserId.Should().Be("u1"); removal.RoleName.Should().Be("Paramedic"); removal.TypeCode.Should().Be("NREMT-P"); removal.GraceDeadline.Should().Be(Today.AddDays(-1));
			var audit = Audits.Single(a => a.Type == AuditLogTypes.RoleMemberRemovedByCertification);
			audit.Before.Should().Contain("\"PersonnelRoleUserId\":1").And.Contain("NREMT-P"); audit.UserId.Should().Be(CertificationService.SystemUserId);
			_notified.Should().Contain(n => n.UserId == "u1" && n.Message.Contains("removed from the Paramedic role"));

			// Day 30 exactly is still inside grace for u1 had it been today.
			_members.Add(new PersonnelRoleUser { PersonnelRoleUserId = 3, PersonnelRoleId = 12, DepartmentId = Dept, UserId = "u3" });
			AddRecord(1, Today.AddDays(-30), "u3", PersonnelCertificationStatuses.Expired);
			_published.Clear();
			(await _service.RunExpirySweepAsync(Dept, Today)).Removed.Should().Be(0);
			(await _service.RunExpirySweepAsync(Dept, Today.AddDays(1))).Removed.Should().Be(1, "removal on day grace + 1");
		}

		[Test]
		public async Task Departments_in_scope_are_the_union_of_typed_records_unit_records_and_requirements()
		{
			(await _service.GetDepartmentsForSweepAsync()).Should().BeEmpty();
			AddRecord(1, Today);
			_unitRecords.Add(new UnitCertification { UnitCertificationId = 1, UnitId = 7, DepartmentId = 9, DepartmentCertificationTypeId = 2 });
			_requirements.Add(new PersonnelRoleCertificationRequirement { DepartmentId = 11, PersonnelRoleId = 1, DepartmentCertificationTypeId = 1 });
			(await _service.GetDepartmentsForSweepAsync()).Should().Equal(Dept, 9, 11);
		}

		#endregion
	}

	/// <summary>Plan D4: the role-membership gate in PersonnelRolesService (block / warn / off) and the membership audits.</summary>
	[TestFixture]
	public class PersonnelRolesCertificationGateTests
	{
		private const int Dept = 3;
		private Mock<ICertificationService> _certifications;
		private Mock<IPersonnelRoleUsersRepository> _roleUsers;
		private Mock<IPersonnelRolesRepository> _roles;
		private readonly List<AuditEvent> _audits = new List<AuditEvent>();
		private readonly List<PersonnelRoleUser> _memberships = new List<PersonnelRoleUser>();
		private PersonnelRolesService _service;
		private int _enforcement;
		private bool _qualified;

		[SetUp]
		public void SetUp()
		{
			_audits.Clear(); _memberships.Clear(); _enforcement = 2; _qualified = false;
			_certifications = new Mock<ICertificationService>();
			_certifications.Setup(c => c.GetCertificationSettingsAsync(Dept)).ReturnsAsync(() => new DepartmentCertificationSettings { DepartmentId = Dept, EnforcementMode = _enforcement });
			_certifications.Setup(c => c.EvaluateUserForRoleAsync(Dept, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime?>()))
				.ReturnsAsync((int _, int role, string user, DateTime? __) => new RoleCertificationEvaluation { PersonnelRoleId = role, UserId = user, Qualified = _qualified, Violations = _qualified ? new List<CertificationRequirementOutcome>() : new List<CertificationRequirementOutcome> { new CertificationRequirementOutcome { IsMandatory = true, TypeCode = "NREMT-P" } } });
			_roleUsers = new Mock<IPersonnelRoleUsersRepository>();
			_roleUsers.Setup(r => r.GetAllRoleUsersForUserAsync(Dept, "u1")).ReturnsAsync(() => _memberships.Where(m => m.UserId == "u1").ToList());
			_roleUsers.Setup(r => r.GetAllMembersOfRoleAsync(It.IsAny<int>())).ReturnsAsync((int role) => _memberships.Where(m => m.PersonnelRoleId == role).ToList());
			_roleUsers.Setup(r => r.InsertAsync(It.IsAny<PersonnelRoleUser>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((PersonnelRoleUser m, CancellationToken _, bool __) => { m.PersonnelRoleUserId = _memberships.Count + 1; _memberships.Add(m); return m; });
			_roleUsers.Setup(r => r.DeleteAsync(It.IsAny<PersonnelRoleUser>(), It.IsAny<CancellationToken>())).ReturnsAsync((PersonnelRoleUser m, CancellationToken _) => _memberships.Remove(m));
			_roles = new Mock<IPersonnelRolesRepository>();
			_roles.Setup(r => r.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 12, DepartmentId = Dept, Name = "Paramedic" }, new PersonnelRole { PersonnelRoleId = 13, DepartmentId = Dept, Name = "Driver" } });
			var aggregator = new Mock<IEventAggregator>();
			aggregator.Setup(a => a.SendMessage<AuditEvent>(It.IsAny<AuditEvent>())).Callback((AuditEvent a) => _audits.Add(a));
			_service = new PersonnelRolesService(_roles.Object, _roleUsers.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentMembersRepository>(), aggregator.Object, Mock.Of<Resgrid.Model.Repositories.Queries.IUnitOfWork>(), new Lazy<ICertificationService>(() => _certifications.Object));
		}

		[Test]
		public async Task Enforce_blocks_a_new_role_before_anything_changes_and_off_or_warn_let_it_through_with_audits()
		{
			_memberships.Add(new PersonnelRoleUser { PersonnelRoleUserId = 1, PersonnelRoleId = 13, DepartmentId = Dept, UserId = "u1" });
			await FluentActions.Awaiting(() => _service.SetRolesForUserAsync(Dept, "u1", new[] { "12", "13" }, default, "admin")).Should().ThrowAsync<InvalidOperationException>().WithMessage("certifications_role_requirements_unmet");
			_memberships.Should().HaveCount(1, "the existing membership is untouched when the change is refused");

			(await _service.SetRolesForUserAsync(Dept, "u1", new[] { "13" }, default, "admin")).Should().BeTrue("keeping a role the member already holds never re-checks it");

			_enforcement = 1;
			(await _service.CheckRoleMembershipAsync(Dept, "u1", new[] { 12 })).Warnings.Should().HaveCount(1);
			(await _service.SetRolesForUserAsync(Dept, "u1", new[] { "12" }, default, "admin")).Should().BeTrue();
			_memberships.Select(m => m.PersonnelRoleId).Should().Equal(12);
			_audits.Select(a => a.Type).Should().Equal(AuditLogTypes.RoleMemberAdded, AuditLogTypes.RoleMemberRemoved);
			_audits.Should().OnlyContain(a => a.UserId == "admin" && a.DepartmentId == Dept);

			_enforcement = 0;
			(await _service.CheckRoleMembershipAsync(Dept, "u1", new[] { 12, 13 })).Evaluations.Should().BeEmpty("Off evaluates nothing");
		}

		[Test]
		public async Task Saving_a_role_with_members_gates_and_audits_the_newcomers_only()
		{
			_memberships.Add(new PersonnelRoleUser { PersonnelRoleUserId = 1, PersonnelRoleId = 12, DepartmentId = Dept, UserId = "existing" });
			_roles.Setup(r => r.SaveOrUpdateAsync(It.IsAny<PersonnelRole>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((PersonnelRole r, CancellationToken _, bool __) => r);
			var role = new PersonnelRole { PersonnelRoleId = 12, DepartmentId = Dept, Name = "Paramedic", Users = new List<PersonnelRoleUser> { new PersonnelRoleUser { UserId = "existing" }, new PersonnelRoleUser { UserId = "newcomer" } } };
			await FluentActions.Awaiting(() => _service.SaveRoleAsync(role, default, "admin")).Should().ThrowAsync<InvalidOperationException>().WithMessage("certifications_role_requirements_unmet");
			_qualified = true;
			await _service.SaveRoleAsync(role, default, "admin");
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.RoleMemberAdded).Which.After.Should().Contain("newcomer");
		}
	}
}
