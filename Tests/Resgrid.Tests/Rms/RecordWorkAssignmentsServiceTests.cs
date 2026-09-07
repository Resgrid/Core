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
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Work assignments (RMS plan section 5.2, RMS-1D): who may assign, who may acknowledge or complete, the
	/// ETag guard, and the rule that an assignment narrows a work queue but never grants access to a Record.
	/// </summary>
	[TestFixture]
	public class RecordWorkAssignmentsServiceTests
	{
		private const int Dept = 9;
		private const string Officer = "officer";
		private const string Member = "member";

		private List<RmsRecordWorkAssignment> _rows;
		private Mock<IRmsRecordWorkAssignmentsRepository> _assignments;
		private Mock<IRmsOperationalRecordsRepository> _records;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IRmsAccessAuditsRepository> _audits;
		private List<RmsAccessAudit> _audited;
		private Mock<IUnitsService> _units;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IIncidentCommandService> _command;
		private RmsOperationalRecord _record;
		private RecordWorkAssignmentsService _service;

		[SetUp]
		public void SetUp()
		{
			_rows = new List<RmsRecordWorkAssignment>();
			_audited = new List<RmsAccessAudit>();
			_record = new RmsOperationalRecord { RmsOperationalRecordId = "r1", DepartmentId = Dept, State = (int)RmsRecordState.Draft, OwnerUserId = Officer, DefinitionKey = "shift-log" };

			_assignments = new Mock<IRmsRecordWorkAssignmentsRepository>();
			_assignments.Setup(a => a.InsertAsync(It.IsAny<RmsRecordWorkAssignment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordWorkAssignment row, CancellationToken c, bool f) => { _rows.Add(row); return row; });
			_assignments.Setup(a => a.UpdateAsync(It.IsAny<RmsRecordWorkAssignment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordWorkAssignment row, CancellationToken c, bool f) => row);
			_assignments.Setup(a => a.GetByIdForDepartmentAsync(Dept, It.IsAny<string>()))
				.ReturnsAsync((int d, string id) => _rows.FirstOrDefault(r => r.RmsRecordWorkAssignmentId == id));
			_assignments.Setup(a => a.GetForRecordAsync(Dept, It.IsAny<string>()))
				.ReturnsAsync((int d, string recordId) => _rows.Where(r => r.RecordId == recordId).ToList());
			_assignments.Setup(a => a.GetOpenForAssigneesAsync(Dept, It.IsAny<string>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<int>>(), It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
				.ReturnsAsync(() => _rows.Where(r => r.IsOpen).ToList());

			_records = new Mock<IRmsOperationalRecordsRepository>();
			_records.Setup(r => r.GetByIdForDepartmentAsync(Dept, "r1")).ReturnsAsync(() => _record);

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync(Officer, Dept, PermissionTypes.ReviewRecords)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync(Member, Dept, It.IsAny<PermissionTypes>())).ReturnsAsync(false);
			_authorization.Setup(a => a.IsDepartmentAdminAsync(It.IsAny<string>(), Dept)).ReturnsAsync(false);
			_authorization.Setup(a => a.CanCreateSourceCallAsync(It.IsAny<string>(), Dept)).ReturnsAsync(false);

			_audits = new Mock<IRmsAccessAuditsRepository>();
			_audits.Setup(a => a.InsertAsync(It.IsAny<RmsAccessAudit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsAccessAudit audit, CancellationToken c, bool f) => { _audited.Add(audit); return audit; });

			_units = new Mock<IUnitsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_command = new Mock<IIncidentCommandService>();

			_service = new RecordWorkAssignmentsService(_assignments.Object, _records.Object, _authorization.Object, _audits.Object, _units.Object, _groups.Object, _command.Object);
		}

		private static RecordWorkAssignmentInput Input(string assignee = Member) => new RecordWorkAssignmentInput
		{
			RecordId = "r1", AssigneeKind = RmsWorkAssigneeKind.Person, AssigneeUserId = assignee, Purpose = RmsWorkAssignmentPurposes.Complete,
			Note = "Finish the crew section", OriginClient = RmsOriginClient.Responder, SourceContext = new FieldRecordContext { CallId = 501 }
		};

		[Test]
		public async Task Assigning_needs_review_permission_ownership_or_administration_and_is_audited_with_the_origin()
		{
			var assignment = await _service.AssignAsync(Dept, Officer, Input());

			assignment.State.Should().Be((int)RmsWorkAssignmentState.Open);
			assignment.Purpose.Should().Be(RmsWorkAssignmentPurposes.Complete);
			assignment.OriginClient.Should().Be((int)RmsOriginClient.Responder);
			assignment.SourceContextJson.Should().Contain("501");
			_audited.Should().ContainSingle();
			_audited[0].Action.Should().Be((int)RmsAccessAuditAction.Admin);
			_audited[0].DetailJson.Should().Contain("Responder");

			Func<Task> stranger = () => _service.AssignAsync(Dept, "stranger", Input());
			await stranger.Should().ThrowAsync<UnauthorizedAccessException>();

			Func<Task> unknownPurpose = () => _service.AssignAsync(Dept, Officer, new RecordWorkAssignmentInput { RecordId = "r1", AssigneeUserId = Member, Purpose = "shred-it" });
			await unknownPurpose.Should().ThrowAsync<ArgumentException>();

			_authorization.Setup(a => a.IsActiveMemberAsync("former", Dept)).ReturnsAsync(false);
			Func<Task> former = () => _service.AssignAsync(Dept, Officer, Input("former"));
			await former.Should().ThrowAsync<ArgumentException>();

			_record.State = (int)RmsRecordState.Voided;
			Func<Task> voided = () => _service.AssignAsync(Dept, Officer, Input());
			await voided.Should().ThrowAsync<RecordTransitionException>();
		}

		[Test]
		public async Task Only_an_addressee_may_acknowledge_and_the_row_version_guards_the_transition()
		{
			var assignment = await _service.AssignAsync(Dept, Officer, Input());

			Func<Task> notAddressee = () => _service.AcknowledgeAsync(Dept, "someone-else", assignment.RmsRecordWorkAssignmentId, null, null, RmsOriginClient.Responder);
			await notAddressee.Should().ThrowAsync<UnauthorizedAccessException>();

			Func<Task> stale = () => _service.AcknowledgeAsync(Dept, Member, assignment.RmsRecordWorkAssignmentId, 99, null, RmsOriginClient.Responder);
			await stale.Should().ThrowAsync<RecordConcurrencyException>();

			var acknowledged = await _service.AcknowledgeAsync(Dept, Member, assignment.RmsRecordWorkAssignmentId, assignment.RowVersion, null, RmsOriginClient.Responder);
			acknowledged.State.Should().Be((int)RmsWorkAssignmentState.Acknowledged);
			acknowledged.AcknowledgedByUserId.Should().Be(Member);
			acknowledged.RowVersion.Should().Be(2);

			Func<Task> twice = () => _service.AcknowledgeAsync(Dept, Member, assignment.RmsRecordWorkAssignmentId, acknowledged.RowVersion, null, RmsOriginClient.Responder);
			await twice.Should().ThrowAsync<InvalidOperationException>();

			var completed = await _service.CompleteAsync(Dept, Member, assignment.RmsRecordWorkAssignmentId, acknowledged.RowVersion, null, RmsOriginClient.Responder);
			completed.State.Should().Be((int)RmsWorkAssignmentState.Completed);
			completed.IsOpen.Should().BeFalse();

			Func<Task> closed = () => _service.CancelAsync(Dept, Officer, assignment.RmsRecordWorkAssignmentId, null, "changed my mind", RmsOriginClient.Web);
			await closed.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task Unit_and_command_assignees_are_resolved_from_live_staffing_and_the_active_command()
		{
			_units.Setup(u => u.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = Dept, Name = "Engine 7" });
			_units.Setup(u => u.GetLastUnitStateByUnitIdAsync(7)).ReturnsAsync(new UnitState { UnitStateId = 1, UnitId = 7, Roles = new List<UnitStateRole> { new UnitStateRole { UserId = "driver" } } });
			var unitAssignment = await _service.AssignAsync(Dept, Officer, new RecordWorkAssignmentInput { RecordId = "r1", AssigneeKind = RmsWorkAssigneeKind.Unit, AssigneeUnitId = 7 });

			(await _service.IsAssigneeAsync(Dept, "driver", unitAssignment, null)).Should().BeTrue();
			(await _service.IsAssigneeAsync(Dept, "passenger", unitAssignment, null)).Should().BeFalse();

			_record.CallId = 501;
			_command.Setup(c => c.GetActiveCommandForCallAsync(Dept, 501)).ReturnsAsync(new IncidentCommand { IncidentCommandId = "ic1", DepartmentId = Dept, CallId = 501, CurrentCommanderUserId = "ic" });
			_command.Setup(c => c.GetCommandBoardAsync(Dept, 501)).ReturnsAsync(new IncidentCommandBoard { Nodes = new List<CommandStructureNode> { new CommandStructureNode { Name = "Operations", SupervisorUserId = "ops" } } });
			var roleAssignment = await _service.AssignAsync(Dept, Officer, new RecordWorkAssignmentInput { RecordId = "r1", AssigneeKind = RmsWorkAssigneeKind.CommandRole, AssigneeRole = "Operations" });

			(await _service.IsAssigneeAsync(Dept, "ops", roleAssignment, null)).Should().BeTrue();
			(await _service.IsAssigneeAsync(Dept, "ic", roleAssignment, null)).Should().BeTrue("the incident commander holds every command role");
			(await _service.IsAssigneeAsync(Dept, "bystander", roleAssignment, null)).Should().BeFalse();

			_units.Setup(u => u.GetUnitByIdAsync(99)).ReturnsAsync(new Unit { UnitId = 99, DepartmentId = Dept + 1 });
			Func<Task> foreign = () => _service.AssignAsync(Dept, Officer, new RecordWorkAssignmentInput { RecordId = "r1", AssigneeKind = RmsWorkAssigneeKind.Unit, AssigneeUnitId = 99 });
			await foreign.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task The_queue_narrows_but_never_grants_a_record_the_caller_may_no_longer_read()
		{
			await _service.AssignAsync(Dept, Officer, Input());
			_records.Setup(r => r.GetByIdForDepartmentAsync(Dept, "r2")).ReturnsAsync(new RmsOperationalRecord { RmsOperationalRecordId = "r2", DepartmentId = Dept, State = (int)RmsRecordState.Draft, OwnerUserId = Officer });
			await _service.AssignAsync(Dept, Officer, new RecordWorkAssignmentInput { RecordId = "r2", AssigneeUserId = Member });

			_authorization.Setup(a => a.CanUserViewRecordAsync(Member, "r2", Dept)).ReturnsAsync(false);
			var queue = await _service.GetQueueAsync(Dept, Member, new FieldRecordContext(), 50);

			queue.Select(q => q.RecordId).Should().Equal(new[] { "r1" }, "an assignment narrows a queue; visibility still decides what the caller sees");

			var forRecord = await _service.GetForRecordAsync(Dept, Member, "r2");
			forRecord.Should().BeEmpty();
		}
	}
}
