using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Invoicing;

namespace Resgrid.Tests.Services
{
	/// <summary>Workforce &amp; Business Operations plan Phase C (deployment core): lifecycle, roster gates, external-order wrapping and events.</summary>
	[TestFixture]
	public class DeploymentServiceTests
	{
		private const int DeptId = 7;
		private const string Manager = "manager";

		private Mock<IDeploymentRepository> _deployments;
		private Mock<IDeploymentUnitRepository> _units;
		private Mock<IDeploymentPersonnelRepository> _personnel;
		private Mock<IDeploymentEquipmentRepository> _equipment;
		private Mock<IDeploymentAttachmentRepository> _attachments;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUnitsService> _unitsService;
		private Mock<IUserProfileService> _profiles;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<ICertificationService> _certifications;
		private Mock<IContactsService> _contacts;
		private Mock<ICallsService> _calls;
		private Mock<IRecordDeploymentsService> _records;
		private Mock<IDomainEventOutboxService> _outbox;
		private Mock<IEventAggregator> _events;
		private Mock<IPdfProvider> _pdf;
		private List<DomainEventEnvelope> _published;
		private List<AuditEvent> _audits;
		private List<Deployment> _storedDeployments;
		private List<DeploymentUnit> _storedUnits;
		private List<DeploymentPersonnel> _storedPersonnel;
		private List<DeploymentEquipment> _storedEquipment;
		private List<DeploymentAttachment> _storedAttachments;
		private DeploymentService _service;

		[SetUp]
		public void SetUp()
		{
			_deployments = new Mock<IDeploymentRepository>();
			_units = new Mock<IDeploymentUnitRepository>();
			_personnel = new Mock<IDeploymentPersonnelRepository>();
			_equipment = new Mock<IDeploymentEquipmentRepository>();
			_attachments = new Mock<IDeploymentAttachmentRepository>();
			_departments = new Mock<IDepartmentsService>();
			_unitsService = new Mock<IUnitsService>();
			_profiles = new Mock<IUserProfileService>();
			_roles = new Mock<IPersonnelRolesService>();
			_certifications = new Mock<ICertificationService>();
			_contacts = new Mock<IContactsService>();
			_calls = new Mock<ICallsService>();
			_records = new Mock<IRecordDeploymentsService>();
			_outbox = new Mock<IDomainEventOutboxService>();
			_events = new Mock<IEventAggregator>();
			_pdf = new Mock<IPdfProvider>();
			_published = new List<DomainEventEnvelope>();
			_audits = new List<AuditEvent>();
			_storedDeployments = new List<Deployment>();
			_storedUnits = new List<DeploymentUnit>();
			_storedPersonnel = new List<DeploymentPersonnel>();
			_storedEquipment = new List<DeploymentEquipment>();
			_storedAttachments = new List<DeploymentAttachment>();

			_outbox.Setup(o => o.EnqueueAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DomainEventEnvelope>(), It.IsAny<CancellationToken>()))
				.Callback<int, string, DomainEventEnvelope, CancellationToken>((_, __, e, ___) => _published.Add(e)).ReturnsAsync(new DomainEventOutboxEntry());
			_events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback<AuditEvent>(a => _audits.Add(a));
			_pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>())).Returns<string>(html => System.Text.Encoding.UTF8.GetBytes(html));
			_departments.Setup(d => d.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DeptId, Name = "Test County Fire", TimeZone = "Pacific Standard Time" });
			_departments.Setup(d => d.GetAllUsersForDepartmentAsync(DeptId, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(new List<IdentityUser> { new IdentityUser { UserId = "alice" }, new IdentityUser { UserId = "bob" } });
			_profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool _) => new UserProfile { UserId = id, FirstName = id, LastName = "Smith" });
			_profiles.Setup(p => p.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync((List<string> ids) => ids.Select(id => new UserProfile { UserId = id, FirstName = id, LastName = "Smith" }).ToList());
			_unitsService.Setup(u => u.GetUnitsForDepartmentUnlimitedAsync(DeptId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 1, DepartmentId = DeptId, Name = "Engine 1", Type = "Engine" }, new Unit { UnitId = 2, DepartmentId = DeptId, Name = "Engine 2" } });
			_unitsService.Setup(u => u.GetUnitByIdAsync(1)).ReturnsAsync(new Unit { UnitId = 1, DepartmentId = DeptId, Name = "Engine 1" });
			_unitsService.Setup(u => u.GetUnitByIdAsync(2)).ReturnsAsync(new Unit { UnitId = 2, DepartmentId = DeptId, Name = "Engine 2" });
			_unitsService.Setup(u => u.GetRoleByIdAsync(10)).ReturnsAsync(new UnitRole { UnitRoleId = 10, UnitId = 1, Name = "Captain", PersonnelRoleId = 5, PersonnelRoleRequired = true });
			_unitsService.Setup(u => u.GetRolesForUnitAsync(It.IsAny<int>())).ReturnsAsync(new List<UnitRole>());
			_roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), DeptId)).ReturnsAsync(new List<PersonnelRole>());
			_certifications.Setup(c => c.EvaluateUserForRoleAsync(DeptId, 5, It.IsAny<string>(), It.IsAny<DateTime?>())).ReturnsAsync(new RoleCertificationEvaluation { Qualified = true });

			// Repositories keep an in-memory list, echo the saved entity and assign ids like RepositoryBase.
			_deployments.Setup(r => r.SaveOrUpdateAsync(It.IsAny<Deployment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Deployment d, CancellationToken _, bool __) => { d.DeploymentId ??= Guid.NewGuid().ToString(); _storedDeployments.RemoveAll(x => x.DeploymentId == d.DeploymentId); _storedDeployments.Add(d); return d; });
			_deployments.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _storedDeployments.FirstOrDefault(d => d.DeploymentId == id));
			_deployments.Setup(r => r.GetByCallIdAsync(It.IsAny<int>(), DeptId)).ReturnsAsync((int callId, int _) => _storedDeployments.FirstOrDefault(d => d.CallId == callId && !d.IsDeleted));
			_deployments.Setup(r => r.GetByExternalOrderIdAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _storedDeployments.FirstOrDefault(d => d.RmsExternalOrderId == id && !d.IsDeleted));
			_units.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentUnit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DeploymentUnit u, CancellationToken _, bool __) => { u.DeploymentUnitId ??= Guid.NewGuid().ToString(); _storedUnits.RemoveAll(x => x.DeploymentUnitId == u.DeploymentUnitId); _storedUnits.Add(u); return u; });
			_units.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedUnits.Where(u => u.DeploymentId == id).ToList());
			_units.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _storedUnits.FirstOrDefault(u => u.DeploymentUnitId == (string)id));
			_units.Setup(r => r.GetActiveAssignmentsForUnitsAsync(DeptId, It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>())).ReturnsAsync(new List<DeploymentUnit>());
			_personnel.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentPersonnel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DeploymentPersonnel p, CancellationToken _, bool __) => { p.DeploymentPersonnelId ??= Guid.NewGuid().ToString(); _storedPersonnel.RemoveAll(x => x.DeploymentPersonnelId == p.DeploymentPersonnelId); _storedPersonnel.Add(p); return p; });
			_personnel.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedPersonnel.Where(p => p.DeploymentId == id).ToList());
			_personnel.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _storedPersonnel.FirstOrDefault(p => p.DeploymentPersonnelId == (string)id));
			_personnel.Setup(r => r.GetActiveAssignmentsForUsersAsync(DeptId, It.IsAny<IEnumerable<string>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>())).ReturnsAsync(new List<DeploymentPersonnel>());
			_equipment.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentEquipment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DeploymentEquipment e, CancellationToken _, bool __) => { e.DeploymentEquipmentId ??= Guid.NewGuid().ToString(); _storedEquipment.RemoveAll(x => x.DeploymentEquipmentId == e.DeploymentEquipmentId); _storedEquipment.Add(e); return e; });
			_equipment.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedEquipment.Where(e => e.DeploymentId == id).ToList());
			_equipment.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _storedEquipment.FirstOrDefault(e => e.DeploymentEquipmentId == (string)id));
			_attachments.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DeploymentAttachment a, CancellationToken _, bool __) => { if (a.DeploymentAttachmentId == 0) a.DeploymentAttachmentId = _storedAttachments.Count + 1; _storedAttachments.RemoveAll(x => x.DeploymentAttachmentId == a.DeploymentAttachmentId); _storedAttachments.Add(a); return a; });
			_attachments.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedAttachments.Where(a => a.DeploymentId == id && !a.IsDeleted).ToList());
			_attachments.Setup(r => r.GetByIdWithDataAsync(It.IsAny<int>())).ReturnsAsync((int id) => _storedAttachments.FirstOrDefault(a => a.DeploymentAttachmentId == id));
			_attachments.Setup(r => r.GetMetadataByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => { var a = _storedAttachments.FirstOrDefault(x => x.DeploymentAttachmentId == id); return a == null ? null : new DeploymentAttachment { DeploymentAttachmentId = a.DeploymentAttachmentId, DeploymentId = a.DeploymentId, DepartmentId = a.DepartmentId, AttachmentType = a.AttachmentType, Name = a.Name, FileName = a.FileName, FileType = a.FileType, FileSize = a.FileSize, IsDeleted = a.IsDeleted }; });
			_attachments.Setup(r => r.MarkDeletedAsync(It.IsAny<int>(), DeptId, It.IsAny<CancellationToken>())).ReturnsAsync((int id, int _, CancellationToken __) => { var a = _storedAttachments.FirstOrDefault(x => x.DeploymentAttachmentId == id && !x.IsDeleted); if (a == null) return 0; a.IsDeleted = true; return 1; });

			_service = new DeploymentService(_deployments.Object, _units.Object, _personnel.Object, _equipment.Object, _attachments.Object, _departments.Object, _unitsService.Object, _profiles.Object,
				_roles.Object, _certifications.Object, _contacts.Object, _calls.Object, _records.Object, _outbox.Object, _events.Object, _pdf.Object, null);
		}

		private Task<Deployment> NewDeploymentAsync(string name = "Ridge Fire", DeploymentFinanceModes mode = DeploymentFinanceModes.Billable) =>
			_service.SaveDeploymentAsync(new Deployment { DepartmentId = DeptId, Name = name, FinanceMode = (int)mode, StartOn = new DateTime(2026, 9, 20, 14, 0, 0, DateTimeKind.Utc), EndOn = new DateTime(2026, 9, 27, 14, 0, 0, DateTimeKind.Utc) }, Manager, "127.0.0.1", "test");

		[Test]
		public async Task Creating_a_deployment_starts_planned_publishes_created_and_audits()
		{
			var saved = await NewDeploymentAsync();

			saved.DeploymentId.Should().NotBeNullOrEmpty();
			saved.Status.Should().Be((int)DeploymentStatuses.Planned);
			saved.AddedByUserId.Should().Be(Manager);
			_published.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.DeploymentCreated && e.AggregateId == saved.DeploymentId);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.DeploymentCreated);
		}

		[Test]
		public async Task Saving_rejects_a_blank_name_and_an_inverted_window()
		{
			(await FluentActions.Awaiting(() => _service.SaveDeploymentAsync(new Deployment { DepartmentId = DeptId, Name = " " }, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_name_required");
			(await FluentActions.Awaiting(() => _service.SaveDeploymentAsync(new Deployment { DepartmentId = DeptId, Name = "x", StartOn = DateTime.UtcNow, EndOn = DateTime.UtcNow.AddDays(-1) }, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_window_invalid");
		}

		[Test]
		public async Task A_call_can_back_only_one_deployment()
		{
			_calls.Setup(c => c.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 42, DepartmentId = DeptId });
			await _service.SaveDeploymentAsync(new Deployment { DepartmentId = DeptId, Name = "First", CallId = 42 }, Manager, null, null);

			(await FluentActions.Awaiting(() => _service.SaveDeploymentAsync(new Deployment { DepartmentId = DeptId, Name = "Second", CallId = 42 }, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_call_already_linked");
		}

		[Test]
		public void Status_transitions_move_forward_only_and_terminal_states_are_final()
		{
			DeploymentService.IsValidTransition(DeploymentStatuses.Planned, DeploymentStatuses.Standby).Should().BeTrue();
			DeploymentService.IsValidTransition(DeploymentStatuses.Planned, DeploymentStatuses.Active).Should().BeTrue("a step may be skipped forward");
			DeploymentService.IsValidTransition(DeploymentStatuses.Active, DeploymentStatuses.Standby).Should().BeFalse("never backward");
			DeploymentService.IsValidTransition(DeploymentStatuses.Demobilizing, DeploymentStatuses.Cancelled).Should().BeTrue();
			DeploymentService.IsValidTransition(DeploymentStatuses.Completed, DeploymentStatuses.Active).Should().BeFalse();
			DeploymentService.IsValidTransition(DeploymentStatuses.Cancelled, DeploymentStatuses.Planned).Should().BeFalse();
		}

		[Test]
		public async Task Status_change_publishes_old_and_new_status_and_stamps_the_window()
		{
			var saved = await _service.SaveDeploymentAsync(new Deployment { DepartmentId = DeptId, Name = "Standby team" }, Manager, null, null);
			_published.Clear();

			var active = await _service.SetDeploymentStatusAsync(saved.DeploymentId, DeptId, DeploymentStatuses.Active, Manager, null, null);
			active.Status.Should().Be((int)DeploymentStatuses.Active);
			active.StartOn.Should().NotBeNull("activation starts the window when none was set");
			active.StatusChangedOn.Should().NotBeNull();
			var payload = JObject.FromObject(_published.Single(e => e.Trigger == WorkflowTriggerEventType.DeploymentStatusChanged).Payload);
			payload["OldStatus"].Value<int>().Should().Be((int)DeploymentStatuses.Planned);
			payload["Status"].Value<int>().Should().Be((int)DeploymentStatuses.Active);

			(await FluentActions.Awaiting(() => _service.SetDeploymentStatusAsync(saved.DeploymentId, DeptId, DeploymentStatuses.Planned, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_status_transition_invalid");
			var completed = await _service.SetDeploymentStatusAsync(saved.DeploymentId, DeptId, DeploymentStatuses.Completed, Manager, null, null);
			completed.EndOn.Should().NotBeNull();
		}

		[Test]
		public async Task Roster_seats_units_and_people_publishes_roster_changes_and_refuses_duplicates()
		{
			var d = await NewDeploymentAsync();
			_published.Clear();

			var unit = await _service.AddUnitAsync(d.DeploymentId, DeptId, 1, "E1", null, Manager, null, null);
			unit.Unit.UnitName.Should().Be("Engine 1");
			var person = await _service.AddPersonnelAsync(d.DeploymentId, DeptId, new DeploymentPersonnelInput { UserId = "alice", DeploymentUnitId = unit.Unit.DeploymentUnitId, CallSign = "E1-A" }, Manager, null, null);
			person.Personnel.Should().NotBeNull();
			person.Personnel.DisplayName.Should().Be("alice Smith");

			(await FluentActions.Awaiting(() => _service.AddUnitAsync(d.DeploymentId, DeptId, 1, null, null, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_unit_already_rostered");
			(await FluentActions.Awaiting(() => _service.AddPersonnelAsync(d.DeploymentId, DeptId, new DeploymentPersonnelInput { UserId = "alice" }, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_user_already_rostered");
			(await FluentActions.Awaiting(() => _service.AddPersonnelAsync(d.DeploymentId, DeptId, new DeploymentPersonnelInput { UserId = "stranger" }, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_user_not_in_department");

			_published.Where(e => e.Trigger == WorkflowTriggerEventType.DeploymentRosterChanged).Should().HaveCount(2);
			var payload = JObject.FromObject(_published.Last().Payload);
			payload["SubjectType"].Value<int>().Should().Be((int)DeploymentTimeSubjectTypes.Personnel);
			payload["RosterAction"].Value<string>().Should().Be("Added");
			payload["SubjectName"].Value<string>().Should().Be("alice Smith");
			(await _service.GetCrewSizeForUnitAsync(unit.Unit.DeploymentUnitId, DeptId)).Should().Be(1);
			(await _service.IsRosteredAsync(d.DeploymentId, DeptId, "alice")).Should().BeTrue();
			(await _service.IsRosteredAsync(d.DeploymentId, DeptId, "bob")).Should().BeFalse();
		}

		[Test]
		public async Task Removing_a_unit_keeps_its_seats_but_unlinks_them()
		{
			var d = await NewDeploymentAsync();
			var unit = await _service.AddUnitAsync(d.DeploymentId, DeptId, 1, null, null, Manager, null, null);
			await _service.AddPersonnelAsync(d.DeploymentId, DeptId, new DeploymentPersonnelInput { UserId = "alice", DeploymentUnitId = unit.Unit.DeploymentUnitId }, Manager, null, null);

			(await _service.RemoveUnitAsync(unit.Unit.DeploymentUnitId, DeptId, Manager, null, null)).Should().BeTrue();

			var full = await _service.GetDeploymentByIdAsync(d.DeploymentId, DeptId);
			full.Units.Single().IsActive.Should().BeFalse();
			full.Personnel.Single().IsActive.Should().BeTrue();
			full.Personnel.Single().DeploymentUnitId.Should().BeNull();
		}

		[Test]
		public async Task A_required_seat_role_blocks_unless_forced_and_certification_violations_flow_as_warnings()
		{
			var d = await NewDeploymentAsync();
			var unit = await _service.AddUnitAsync(d.DeploymentId, DeptId, 1, null, null, Manager, null, null);
			_certifications.Setup(c => c.EvaluateUserForRoleAsync(DeptId, 5, "bob", It.IsAny<DateTime?>())).ReturnsAsync(new RoleCertificationEvaluation
			{
				Qualified = false,
				Violations = { new CertificationRequirementOutcome { TypeCode = "EMT", TypeName = "EMT-Basic", IsMandatory = true } },
				Outcomes = { new CertificationRequirementOutcome { TypeCode = "CPR", TypeName = "CPR", Satisfied = true, ExpiresOn = new DateTime(2026, 9, 22) } }
			});

			var blocked = await _service.AddPersonnelAsync(d.DeploymentId, DeptId, new DeploymentPersonnelInput { UserId = "bob", DeploymentUnitId = unit.Unit.DeploymentUnitId, UnitRoleId = 10 }, Manager, null, null);
			blocked.Personnel.Should().BeNull("the seat requires a role the member does not hold");
			blocked.HasBlockingWarnings.Should().BeTrue();
			blocked.Warnings.Select(w => w.Code).Should().Contain(new[] { DeploymentRosterWarning.RoleNotHeld, DeploymentRosterWarning.CertificationMissing, DeploymentRosterWarning.CertificationExpiring });
			_storedPersonnel.Should().BeEmpty();

			var forced = await _service.AddPersonnelAsync(d.DeploymentId, DeptId, new DeploymentPersonnelInput { UserId = "bob", DeploymentUnitId = unit.Unit.DeploymentUnitId, UnitRoleId = 10, Force = true }, Manager, null, null);
			forced.Personnel.Should().NotBeNull();
			forced.Warnings.Should().OnlyContain(w => !w.Blocking, "once written the warnings are advisory");

			_roles.Setup(r => r.GetRolesForUserAsync("alice", DeptId)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 5 } });
			var seated = await _service.AddPersonnelAsync(d.DeploymentId, DeptId, new DeploymentPersonnelInput { UserId = "alice", DeploymentUnitId = unit.Unit.DeploymentUnitId, UnitRoleId = 10 }, Manager, null, null);
			seated.Personnel.Should().NotBeNull();
			seated.Warnings.Should().BeEmpty();
		}

		[Test]
		public async Task Schedule_conflicts_surface_as_warnings_without_blocking()
		{
			var d = await NewDeploymentAsync();
			_personnel.Setup(r => r.GetActiveAssignmentsForUsersAsync(DeptId, It.IsAny<IEnumerable<string>>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), d.DeploymentId))
				.ReturnsAsync(new List<DeploymentPersonnel> { new DeploymentPersonnel { UserId = "alice", DeploymentId = "other" } });

			var warnings = await _service.GetRosterWarningsAsync(d.DeploymentId, DeptId, new[] { "alice", "bob" }, null);
			warnings.Should().ContainSingle(w => w.Code == DeploymentRosterWarning.ScheduleConflict && w.SubjectId == "alice" && w.Detail == "other");

			var result = await _service.AddPersonnelAsync(d.DeploymentId, DeptId, new DeploymentPersonnelInput { UserId = "alice" }, Manager, null, null);
			result.Personnel.Should().NotBeNull();
			result.Warnings.Should().ContainSingle(w => w.Code == DeploymentRosterWarning.ScheduleConflict);
		}

		[Test]
		public async Task A_closed_deployment_locks_its_roster()
		{
			var d = await NewDeploymentAsync();
			await _service.SetDeploymentStatusAsync(d.DeploymentId, DeptId, DeploymentStatuses.Cancelled, Manager, null, null);

			(await FluentActions.Awaiting(() => _service.AddUnitAsync(d.DeploymentId, DeptId, 1, null, null, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_closed");
			await FluentActions.Awaiting(() => _service.DeleteDeploymentAsync(d.DeploymentId, DeptId, Manager, null, null)).Should().NotThrowAsync();
		}

		[Test]
		public async Task Equipment_without_the_inventory_module_is_a_free_text_row()
		{
			var d = await NewDeploymentAsync();
			var added = await _service.AddEquipmentAsync(d.DeploymentId, DeptId, new DeploymentEquipmentInput { FreeTextName = "Portable pump", IssueFromInventory = true }, Manager, null, null);
			added.Equipment.FreeTextName.Should().Be("Portable pump");
			added.Equipment.IssuedOn.Should().BeNull("no inventory module is registered");
			added.Warnings.Should().BeEmpty();
			(await FluentActions.Awaiting(() => _service.AddEquipmentAsync(d.DeploymentId, DeptId, new DeploymentEquipmentInput(), Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_equipment_required");

			(await _service.ReturnEquipmentAsync(added.Equipment.DeploymentEquipmentId, DeptId, Manager, null, null)).Should().BeTrue();
			_storedEquipment.Single().ReturnedOn.Should().NotBeNull();
		}

		[Test]
		public async Task External_order_wrapping_prefills_identifiers_and_seats_accepted_fills_once()
		{
			_records.Setup(r => r.GetAsync(DeptId, Manager, "order-1", false)).ReturnsAsync(new RecordDeploymentAggregate
			{
				Order = new RmsExternalOrder { RmsExternalOrderId = "order-1", DepartmentId = DeptId, OrderNumber = "O-1234", IncidentName = "Ridge Fire", IncidentNumber = "CA-BTU-012345", CostCode = "CC-1", IncidentCountry = "US", IncidentSubdivision = "CA", TimeZoneId = "Pacific Standard Time", CurrencyCode = "USD", MeasurementSystem = "imperial" },
				Fills = new List<RmsExternalOrderFill>
				{
					new RmsExternalOrderFill { RmsExternalOrderFillId = "fill-1", RequestNumber = "E-12", Status = (int)RmsDeploymentFillStatus.Accepted, AssignedUserId = "alice", AssignedUnitId = 1, Position = "ENGB" },
					new RmsExternalOrderFill { RmsExternalOrderFillId = "fill-2", RequestNumber = "E-13", Status = (int)RmsDeploymentFillStatus.Declined, AssignedUserId = "bob" },
					new RmsExternalOrderFill { RmsExternalOrderFillId = "fill-3", RequestNumber = "E-14", Status = (int)RmsDeploymentFillStatus.Mobilized, AssignedUserId = "bob", AssignedUnitId = 1 }
				}
			});

			var d = await _service.CreateFromExternalOrderAsync(DeptId, new ExternalOrderDeploymentInput { RmsExternalOrderId = "order-1", FinanceMode = DeploymentFinanceModes.CostRecovery }, Manager, null, null);

			d.Name.Should().Be("Ridge Fire");
			d.RmsExternalOrderId.Should().Be("order-1");
			d.FinanceMode.Should().Be((int)DeploymentFinanceModes.CostRecovery);
			d.IncidentNumber.Should().Be("CA-BTU-012345");
			d.ResourceOrderNumber.Should().Be("O-1234");
			d.RequestNumber.Should().BeNull("multi-request identity stays on the fills");
			d.CostCode.Should().Be("CC-1");
			d.HostCountry.Should().Be("US");
			d.LocalTimeZoneId.Should().Be("Pacific Standard Time");
			d.Currency.Should().Be("USD");
			d.CallId.Should().BeNull("no call was requested");
			d.Units.Should().ContainSingle(u => u.UnitId == 1);
			d.Personnel.Select(p => p.UserId).Should().BeEquivalentTo(new[] { "alice", "bob" }, "declined fills are not seated");
			d.Personnel.Single(p => p.UserId == "alice").RmsExternalOrderFillId.Should().Be("fill-1");
			d.Personnel.Single(p => p.UserId == "alice").CertificationCode.Should().Be("ENGB");
			d.Personnel.Should().OnlyContain(p => p.DeploymentUnitId == d.Units.Single().DeploymentUnitId);

			(await FluentActions.Awaiting(() => _service.CreateFromExternalOrderAsync(DeptId, new ExternalOrderDeploymentInput { RmsExternalOrderId = "order-1" }, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_external_order_already_linked");
			_calls.Verify(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task External_order_wrapping_can_create_the_call_with_the_contact_link()
		{
			_records.Setup(r => r.GetAsync(DeptId, Manager, "order-2", false)).ReturnsAsync(new RecordDeploymentAggregate
			{
				Order = new RmsExternalOrder { RmsExternalOrderId = "order-2", DepartmentId = DeptId, OrderNumber = "O-2", IncidentName = "Creek Fire", RequestingAgency = "CAL FIRE" },
				Fills = new List<RmsExternalOrderFill>()
			});
			_contacts.Setup(c => c.GetContactByIdAsync("contact-1")).ReturnsAsync(new Contact { ContactId = "contact-1", DepartmentId = DeptId, CompanyName = "CAL FIRE" });
			Call savedCall = null;
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken _) => { c.CallId = 77; savedCall = c; return c; });
			_calls.Setup(c => c.GetCallByIdAsync(77, It.IsAny<bool>())).ReturnsAsync(() => savedCall);

			var d = await _service.CreateFromExternalOrderAsync(DeptId, new ExternalOrderDeploymentInput { RmsExternalOrderId = "order-2", CreateCall = true, CallType = "Mutual Aid", CallPriority = 2, ContactId = "contact-1" }, Manager, null, null);

			d.CallId.Should().Be(77);
			savedCall.DepartmentId.Should().Be(DeptId);
			savedCall.ReportingUserId.Should().Be(Manager);
			savedCall.Name.Should().Be("Creek Fire");
			savedCall.NatureOfCall.Should().Contain("O-2");
			savedCall.Type.Should().Be("Mutual Aid");
			savedCall.Priority.Should().Be(2);
			savedCall.Contacts.Should().ContainSingle(c => c.ContactId == "contact-1" && c.CallContactType == 0);
			savedCall.Notes.Should().Contain("CAL FIRE");
		}

		[Test]
		public async Task Manifest_renders_the_roster_and_files_as_a_manifest_attachment()
		{
			var d = await NewDeploymentAsync();
			var unit = await _service.AddUnitAsync(d.DeploymentId, DeptId, 1, "E1", null, Manager, null, null);
			await _service.AddPersonnelAsync(d.DeploymentId, DeptId, new DeploymentPersonnelInput { UserId = "alice", DeploymentUnitId = unit.Unit.DeploymentUnitId, CertificationCode = "ENGB" }, Manager, null, null);

			var html = await _service.RenderManifestHtmlAsync(d.DeploymentId, DeptId);
			html.Should().Contain("Engine 1").And.Contain("alice Smith").And.Contain("ENGB").And.Contain("Test County Fire");

			var attachment = await _service.GenerateManifestAsync(d.DeploymentId, DeptId, Manager, null, null);
			attachment.AttachmentType.Should().Be((int)DeploymentAttachmentTypes.Manifest);
			attachment.FileType.Should().Be("application/pdf");
			attachment.Data.Should().BeNull("the returned row carries no bytes");
			_storedAttachments.Single().Data.Should().NotBeEmpty();
			_storedAttachments.Single().FileSize.Should().Be(_storedAttachments.Single().Data.Length);
			_audits.Should().Contain(a => a.Type == AuditLogTypes.DeploymentAttachmentAdded);

			(await _service.GetAttachmentsAsync(d.DeploymentId, DeptId)).Should().ContainSingle();
			(await _service.GetAttachmentAsync(attachment.DeploymentAttachmentId, DeptId, true)).Data.Should().NotBeEmpty();
			(await _service.GetAttachmentAsync(attachment.DeploymentAttachmentId, DeptId, false)).Data.Should().BeNull();
		}

		[Test]
		public async Task Attachments_reject_empty_and_oversized_uploads()
		{
			var d = await NewDeploymentAsync();
			(await FluentActions.Awaiting(() => _service.SaveAttachmentAsync(new DeploymentAttachment { DeploymentId = d.DeploymentId, DepartmentId = DeptId, FileName = "x.pdf" }, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_attachment_empty");
			(await FluentActions.Awaiting(() => _service.SaveAttachmentAsync(new DeploymentAttachment { DeploymentId = d.DeploymentId, DepartmentId = DeptId, FileName = "x.pdf", Data = new byte[DeploymentService.MaxAttachmentBytes + 1] }, Manager, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("deployments_attachment_too_large");
		}

		[Test]
		public async Task Deleting_an_attachment_flags_the_row_without_moving_its_bytes()
		{
			var d = await NewDeploymentAsync();
			var saved = await _service.SaveAttachmentAsync(new DeploymentAttachment { DeploymentId = d.DeploymentId, DepartmentId = DeptId, FileName = "map.pdf", FileType = "application/pdf", Data = new byte[] { 1, 2, 3 } }, Manager, null, null);
			_attachments.Invocations.Clear();

			(await _service.DeleteAttachmentAsync(saved.DeploymentAttachmentId, DeptId, Manager, null, null)).Should().BeTrue();

			_storedAttachments.Single().IsDeleted.Should().BeTrue();
			_attachments.Verify(r => r.GetByIdWithDataAsync(It.IsAny<int>()), Times.Never, "the soft delete reads metadata only");
			_attachments.Verify(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentAttachment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never, "the flag is set in place");
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.DeploymentAttachmentRemoved).Which.After.Should().NotContain("\"Data\":\"AQID\"");
			(await _service.DeleteAttachmentAsync(saved.DeploymentAttachmentId, DeptId, Manager, null, null)).Should().BeFalse("a deleted row is not deleted twice");
			(await _service.DeleteAttachmentAsync(saved.DeploymentAttachmentId, DeptId + 1, Manager, null, null)).Should().BeFalse("another department's id is not found");
		}

		[Test]
		public async Task Member_scope_lists_only_rostered_deployments()
		{
			var mine = await NewDeploymentAsync("Mine");
			var other = await NewDeploymentAsync("Other");
			await _service.AddPersonnelAsync(mine.DeploymentId, DeptId, new DeploymentPersonnelInput { UserId = "alice" }, Manager, null, null);
			_personnel.Setup(r => r.GetForUserAsync(DeptId, "alice")).ReturnsAsync(() => _storedPersonnel.Where(p => p.UserId == "alice").ToList());
			_deployments.Setup(r => r.GetByIdsAsync(DeptId, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int _, IEnumerable<string> ids) => _storedDeployments.Where(x => ids.Contains(x.DeploymentId)).ToList());

			var list = await _service.GetDeploymentsForUserAsync(DeptId, "alice", true);
			list.Select(x => x.DeploymentId).Should().BeEquivalentTo(new[] { mine.DeploymentId });
			other.DeploymentId.Should().NotBe(mine.DeploymentId);
		}

		[Test]
		public async Task Every_deployment_save_projects_the_row_for_search()
		{
			var projected = new List<string>();
			var projections = new Mock<ISearchProjectionService>();
			projections.Setup(p => p.ProjectDeploymentAsync(It.IsAny<Deployment>(), It.IsAny<CancellationToken>()))
				.Callback((Deployment d, CancellationToken _) => projected.Add(d.Name)).Returns(Task.CompletedTask);
			_service = new DeploymentService(_deployments.Object, _units.Object, _personnel.Object, _equipment.Object, _attachments.Object, _departments.Object, _unitsService.Object, _profiles.Object,
				_roles.Object, _certifications.Object, _contacts.Object, _calls.Object, _records.Object, _outbox.Object, _events.Object, _pdf.Object, null,
				searchProjections: new Lazy<ISearchProjectionService>(() => projections.Object));

			var saved = await NewDeploymentAsync("Ridge Fire");
			saved.Name = "Ridge Fire Complex";
			await _service.SaveDeploymentAsync(saved, Manager, null, null);

			projected.Should().Equal(new[] { "Ridge Fire", "Ridge Fire Complex" }, "a created and an edited deployment reach search without waiting for a rebuild");
		}

		[Test]
		public async Task Header_rows_by_ids_skip_blank_ids_and_missing_rows()
		{
			var mine = await NewDeploymentAsync("Mine");
			_deployments.Setup(r => r.GetByIdsAsync(DeptId, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int _, IEnumerable<string> ids) => _storedDeployments.Where(x => ids.Contains(x.DeploymentId)).ToList());

			var headers = await _service.GetDeploymentsByIdsAsync(DeptId, new[] { mine.DeploymentId, " ", null, "missing" });

			headers.Select(h => h.DeploymentId).Should().Equal(mine.DeploymentId);
			(await _service.GetDeploymentsByIdsAsync(DeptId, null)).Should().BeEmpty();
			_deployments.Verify(r => r.GetByIdsAsync(DeptId, It.IsAny<IEnumerable<string>>()), Times.Once, "blank input never reaches the repository");
		}
	}
}
