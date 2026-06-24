using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Covers the PAR (personnel accountability) firing point: <see cref="IncidentCommandService.EvaluateCriticalParAsync"/>
	/// raises <see cref="CriticalParDetectedEvent"/> once per member each time they transition into Critical, deduped
	/// via a <see cref="CommandLogEntryType.ParCritical"/> timeline marker, and re-fires after a fresh check-in.
	/// </summary>
	[TestFixture]
	public class IncidentCommandServiceParTests
	{
		private const int Dept = 10;
		private const int CallId = 1;

		private Mock<IIncidentCommandRepository> _commandRepo;
		private Mock<ICommandStructureNodeRepository> _nodeRepo;
		private Mock<IResourceAssignmentRepository> _assignmentRepo;
		private Mock<ITacticalObjectiveRepository> _objectiveRepo;
		private Mock<IIncidentTimerRepository> _timerRepo;
		private Mock<IIncidentMapAnnotationRepository> _annotationRepo;
		private Mock<ICommandLogEntryRepository> _logRepo;
		private Mock<ICommandTransferRepository> _transferRepo;
		private Mock<ICommandsService> _commandsService;
		private Mock<ICallsService> _callsService;
		private Mock<ICheckInTimerService> _checkInTimerService;
		private Mock<IIncidentVoiceService> _voiceService;
		private Mock<IIncidentRoleAssignmentRepository> _roleRepo;
		private Mock<IEventAggregator> _eventAggregator;
		private Mock<ICoreEventService> _coreEventService;
		private IncidentCommandService _service;

		[SetUp]
		public void SetUp()
		{
			_commandRepo = new Mock<IIncidentCommandRepository>();
			_nodeRepo = new Mock<ICommandStructureNodeRepository>();
			_assignmentRepo = new Mock<IResourceAssignmentRepository>();
			_objectiveRepo = new Mock<ITacticalObjectiveRepository>();
			_timerRepo = new Mock<IIncidentTimerRepository>();
			_annotationRepo = new Mock<IIncidentMapAnnotationRepository>();
			_logRepo = new Mock<ICommandLogEntryRepository>();
			_transferRepo = new Mock<ICommandTransferRepository>();
			_commandsService = new Mock<ICommandsService>();
			_callsService = new Mock<ICallsService>();
			_checkInTimerService = new Mock<ICheckInTimerService>();
			_voiceService = new Mock<IIncidentVoiceService>();
			_roleRepo = new Mock<IIncidentRoleAssignmentRepository>();
			_eventAggregator = new Mock<IEventAggregator>();
			_coreEventService = new Mock<ICoreEventService>();

			// Timeline entries are append-only inserts; echo back the entry so WriteLogAsync resolves a non-null result.
			_logRepo.Setup(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CommandLogEntry e, CancellationToken ct, bool b) => e);

			_service = new IncidentCommandService(_commandRepo.Object, _nodeRepo.Object, _assignmentRepo.Object,
				_objectiveRepo.Object, _timerRepo.Object, _annotationRepo.Object, _logRepo.Object, _transferRepo.Object,
				_commandsService.Object, _callsService.Object, _checkInTimerService.Object, _voiceService.Object,
				_roleRepo.Object, _eventAggregator.Object, _coreEventService.Object);
		}

		private void ArrangeCall(bool checkInTimersEnabled = true, int departmentId = Dept)
		{
			_callsService.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = CallId, DepartmentId = departmentId, CheckInTimersEnabled = checkInTimersEnabled, State = (int)CallStates.Active });
		}

		private void ArrangeActiveCommand(DateTime? establishedOn = null)
		{
			_commandRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentCommand>
			{
				new IncidentCommand
				{
					IncidentCommandId = "ic1",
					DepartmentId = Dept,
					CallId = CallId,
					Status = (int)IncidentCommandStatus.Active,
					EstablishedOn = establishedOn ?? DateTime.UtcNow.AddMinutes(-30)
				}
			});
		}

		private void ArrangeStatuses(params PersonnelCallCheckInStatus[] statuses)
		{
			_checkInTimerService.Setup(x => x.GetCallPersonnelCheckInStatusesAsync(It.IsAny<Call>()))
				.ReturnsAsync(new List<PersonnelCallCheckInStatus>(statuses));
		}

		private void ArrangeTimeline(params CommandLogEntry[] entries)
		{
			_logRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<CommandLogEntry>(entries));
		}

		private static PersonnelCallCheckInStatus Critical(string userId, DateTime? lastCheckIn = null) => new PersonnelCallCheckInStatus
		{
			UserId = userId,
			FullName = "Firefighter " + userId,
			Status = "Critical",
			NeedsCheckIn = true,
			MinutesRemaining = -3,
			LastCheckIn = lastCheckIn
		};

		[Test]
		public async Task EvaluateCriticalParAsync_RaisesEventAndWritesMarker_OnFirstTransitionToCritical()
		{
			ArrangeCall();
			ArrangeActiveCommand();
			ArrangeStatuses(Critical("user1"));
			ArrangeTimeline(); // no prior markers

			var result = await _service.EvaluateCriticalParAsync(Dept, CallId);

			result.Should().ContainSingle().Which.Should().Be("user1");
			_eventAggregator.Verify(x => x.SendMessage(It.Is<CriticalParDetectedEvent>(
				e => e.UserId == "user1" && e.CallId == CallId && e.DepartmentId == Dept)), Times.Once);
			_logRepo.Verify(x => x.InsertAsync(
				It.Is<CommandLogEntry>(e => e.EntryType == (int)CommandLogEntryType.ParCritical && e.UserId == "user1"),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task EvaluateCriticalParAsync_Deduped_WhenMarkerAlreadyExistsForEpisode()
		{
			ArrangeCall();
			ArrangeActiveCommand(establishedOn: DateTime.UtcNow.AddMinutes(-30));
			ArrangeStatuses(Critical("user1")); // never checked in -> baseline is EstablishedOn
			// A marker newer than the baseline means this episode was already alerted.
			ArrangeTimeline(new CommandLogEntry
			{
				EntryType = (int)CommandLogEntryType.ParCritical,
				UserId = "user1",
				CallId = CallId,
				OccurredOn = DateTime.UtcNow.AddMinutes(-5)
			});

			var result = await _service.EvaluateCriticalParAsync(Dept, CallId);

			result.Should().BeEmpty();
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CriticalParDetectedEvent>()), Times.Never);
			_logRepo.Verify(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task EvaluateCriticalParAsync_Refires_AfterMemberChecksInAgain()
		{
			ArrangeCall();
			ArrangeActiveCommand();
			// The member checked in 1 min ago (newer than the old marker) and has lapsed Critical again.
			ArrangeStatuses(Critical("user1", lastCheckIn: DateTime.UtcNow.AddMinutes(-1)));
			ArrangeTimeline(new CommandLogEntry
			{
				EntryType = (int)CommandLogEntryType.ParCritical,
				UserId = "user1",
				CallId = CallId,
				OccurredOn = DateTime.UtcNow.AddMinutes(-10) // older than the latest check-in
			});

			var result = await _service.EvaluateCriticalParAsync(Dept, CallId);

			result.Should().ContainSingle().Which.Should().Be("user1");
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CriticalParDetectedEvent>()), Times.Once);
		}

		[Test]
		public async Task EvaluateCriticalParAsync_NoEvent_WhenNobodyIsCritical()
		{
			ArrangeCall();
			ArrangeActiveCommand();
			ArrangeStatuses(new PersonnelCallCheckInStatus { UserId = "user1", Status = "Green", MinutesRemaining = 12 });
			ArrangeTimeline();

			var result = await _service.EvaluateCriticalParAsync(Dept, CallId);

			result.Should().BeEmpty();
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CriticalParDetectedEvent>()), Times.Never);
		}

		[Test]
		public async Task EvaluateCriticalParAsync_OnlyFlagsCriticalMembers_WhenMixed()
		{
			ArrangeCall();
			ArrangeActiveCommand();
			ArrangeStatuses(
				Critical("user1"),
				new PersonnelCallCheckInStatus { UserId = "user2", Status = "Warning", MinutesRemaining = 2 },
				Critical("user3"));
			ArrangeTimeline();

			var result = await _service.EvaluateCriticalParAsync(Dept, CallId);

			result.Should().BeEquivalentTo(new[] { "user1", "user3" });
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CriticalParDetectedEvent>()), Times.Exactly(2));
		}

		[Test]
		public async Task EvaluateCriticalParAsync_ReturnsEmpty_WhenNoActiveCommand()
		{
			ArrangeCall();
			_commandRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentCommand>());
			ArrangeStatuses(Critical("user1"));

			var result = await _service.EvaluateCriticalParAsync(Dept, CallId);

			result.Should().BeEmpty();
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CriticalParDetectedEvent>()), Times.Never);
		}

		[Test]
		public async Task EvaluateCriticalParAsync_ReturnsEmpty_WhenCallNotOwnedByDepartment()
		{
			ArrangeCall(departmentId: 99); // belongs to another department
			ArrangeActiveCommand();
			ArrangeStatuses(Critical("user1"));

			var result = await _service.EvaluateCriticalParAsync(Dept, CallId);

			result.Should().BeEmpty();
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CriticalParDetectedEvent>()), Times.Never);
		}

		[Test]
		public async Task EvaluateCriticalParAsync_ReturnsEmpty_WhenCheckInTimersDisabled()
		{
			ArrangeCall(checkInTimersEnabled: false);
			ArrangeActiveCommand();
			ArrangeStatuses(Critical("user1"));

			var result = await _service.EvaluateCriticalParAsync(Dept, CallId);

			result.Should().BeEmpty();
			_eventAggregator.Verify(x => x.SendMessage(It.IsAny<CriticalParDetectedEvent>()), Times.Never);
		}

		// Child-mutation CallId stamping: the authoritative CallId comes from the parent command, never the caller.

		[Test]
		public async Task SaveNodeAsync_StampsCallId_FromParentCommand_NotCallerSupplied()
		{
			// Parent command 'ic1' lives on CallId; the caller supplies a mismatched CallId (999).
			_commandRepo.Setup(x => x.GetByIdAsync("ic1")).ReturnsAsync(new IncidentCommand
			{
				IncidentCommandId = "ic1", DepartmentId = Dept, CallId = CallId, Status = (int)IncidentCommandStatus.Active
			});
			_nodeRepo.Setup(x => x.InsertAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CommandStructureNode n, CancellationToken ct, bool b) => n);

			var node = new CommandStructureNode { IncidentCommandId = "ic1", DepartmentId = Dept, CallId = 999, Name = "Staging" };
			var saved = await _service.SaveNodeAsync(node, "user1");

			saved.Should().NotBeNull();
			saved.CallId.Should().Be(CallId); // stamped from the parent command, not the caller-supplied 999
		}

		[Test]
		public async Task SaveNodeAsync_ReturnsNull_WhenParentCommandBelongsToAnotherDepartment()
		{
			_commandRepo.Setup(x => x.GetByIdAsync("ic1")).ReturnsAsync(new IncidentCommand
			{
				IncidentCommandId = "ic1", DepartmentId = 99, CallId = CallId // another department's command
			});

			var node = new CommandStructureNode { IncidentCommandId = "ic1", DepartmentId = Dept, CallId = CallId, Name = "Staging" };
			var saved = await _service.SaveNodeAsync(node, "user1");

			saved.Should().BeNull();
		}

		[Test]
		public async Task MoveResourceAsync_Moves_WhenTargetNodeOnSameDeptAndCall()
		{
			_assignmentRepo.Setup(x => x.GetByIdAsync("ra1")).ReturnsAsync(new ResourceAssignment
			{
				ResourceAssignmentId = "ra1", DepartmentId = Dept, CallId = CallId, IncidentCommandId = "ic1"
			});
			_nodeRepo.Setup(x => x.GetByIdAsync("node-1")).ReturnsAsync(new CommandStructureNode
			{
				CommandStructureNodeId = "node-1", DepartmentId = Dept, CallId = CallId
			});
			_assignmentRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<ResourceAssignment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ResourceAssignment a, CancellationToken ct, bool b) => a);

			var result = await _service.MoveResourceAsync(Dept, "ra1", "node-1", "user1");

			result.Should().NotBeNull();
			result.CommandStructureNodeId.Should().Be("node-1");
		}

		[Test]
		public async Task MoveResourceAsync_ReturnsNull_WhenTargetNodeOnDifferentCall()
		{
			_assignmentRepo.Setup(x => x.GetByIdAsync("ra1")).ReturnsAsync(new ResourceAssignment
			{
				ResourceAssignmentId = "ra1", DepartmentId = Dept, CallId = CallId, IncidentCommandId = "ic1"
			});
			// Same department, but the lane lives on a different call — must be rejected.
			_nodeRepo.Setup(x => x.GetByIdAsync("node-other")).ReturnsAsync(new CommandStructureNode
			{
				CommandStructureNodeId = "node-other", DepartmentId = Dept, CallId = 999
			});

			var result = await _service.MoveResourceAsync(Dept, "ra1", "node-other", "user1");

			result.Should().BeNull();
			_assignmentRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<ResourceAssignment>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task MoveResourceAsync_ReturnsNull_WhenTargetNodeMissing()
		{
			_assignmentRepo.Setup(x => x.GetByIdAsync("ra1")).ReturnsAsync(new ResourceAssignment
			{
				ResourceAssignmentId = "ra1", DepartmentId = Dept, CallId = CallId, IncidentCommandId = "ic1"
			});
			_nodeRepo.Setup(x => x.GetByIdAsync("ghost")).ReturnsAsync((CommandStructureNode)null);

			var result = await _service.MoveResourceAsync(Dept, "ra1", "ghost", "user1");

			result.Should().BeNull();
		}

		// Offline-first change tracking: every mutation stamps ModifiedOn (the delta "changed since" cursor) and a
		// lane removal is a soft-delete tombstone (DeletedOn), so removals propagate to offline clients on delta sync.

		[Test]
		public async Task SaveNodeAsync_StampsModifiedOn_OnSave()
		{
			_commandRepo.Setup(x => x.GetByIdAsync("ic1")).ReturnsAsync(new IncidentCommand
			{
				IncidentCommandId = "ic1", DepartmentId = Dept, CallId = CallId, Status = (int)IncidentCommandStatus.Active
			});
			_nodeRepo.Setup(x => x.InsertAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CommandStructureNode n, CancellationToken ct, bool b) => n);

			var node = new CommandStructureNode { IncidentCommandId = "ic1", DepartmentId = Dept, CallId = CallId, Name = "Staging" };
			var saved = await _service.SaveNodeAsync(node, "user1");

			saved.Should().NotBeNull();
			saved.ModifiedOn.Should().NotBeNull();
		}

		// Idempotent creates (offline replay): the client generates the GUID PK offline. A brand-new client id must
		// INSERT (not be rejected as a missing-row update); a replayed id must UPDATE the same row (no duplicate);
		// an id owned by another department must be rejected.

		[Test]
		public async Task SaveNodeAsync_WithClientSuppliedId_NotYetPersisted_InsertsWithThatId()
		{
			const string clientId = "client-guid-1";
			_commandRepo.Setup(x => x.GetByIdAsync("ic1")).ReturnsAsync(new IncidentCommand
			{
				IncidentCommandId = "ic1", DepartmentId = Dept, CallId = CallId, Status = (int)IncidentCommandStatus.Active
			});
			_nodeRepo.Setup(x => x.GetByIdAsync(clientId)).ReturnsAsync((CommandStructureNode)null); // not persisted yet
			_nodeRepo.Setup(x => x.InsertAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CommandStructureNode n, CancellationToken ct, bool b) => n);

			var node = new CommandStructureNode { CommandStructureNodeId = clientId, IncidentCommandId = "ic1", DepartmentId = Dept, Name = "Staging" };
			var saved = await _service.SaveNodeAsync(node, "user1");

			saved.Should().NotBeNull();
			saved.CommandStructureNodeId.Should().Be(clientId); // honored the client GUID, did not generate a new one
			_nodeRepo.Verify(x => x.InsertAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_nodeRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task SaveNodeAsync_WithClientSuppliedId_AlreadyPersisted_Updates_WithoutDuplicateInsert()
		{
			const string clientId = "client-guid-1";
			_commandRepo.Setup(x => x.GetByIdAsync("ic1")).ReturnsAsync(new IncidentCommand
			{
				IncidentCommandId = "ic1", DepartmentId = Dept, CallId = CallId, Status = (int)IncidentCommandStatus.Active
			});
			// Replay: the row already exists for this department.
			_nodeRepo.Setup(x => x.GetByIdAsync(clientId)).ReturnsAsync(new CommandStructureNode
			{
				CommandStructureNodeId = clientId, IncidentCommandId = "ic1", DepartmentId = Dept, CallId = CallId, Name = "Staging"
			});
			_nodeRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CommandStructureNode n, CancellationToken ct, bool b) => n);

			var node = new CommandStructureNode { CommandStructureNodeId = clientId, IncidentCommandId = "ic1", DepartmentId = Dept, Name = "Staging (edited)" };
			var saved = await _service.SaveNodeAsync(node, "user1");

			saved.Should().NotBeNull();
			_nodeRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_nodeRepo.Verify(x => x.InsertAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task SaveNodeAsync_WithClientSuppliedId_OwnedByAnotherDepartment_ReturnsNull()
		{
			const string clientId = "client-guid-1";
			_commandRepo.Setup(x => x.GetByIdAsync("ic1")).ReturnsAsync(new IncidentCommand
			{
				IncidentCommandId = "ic1", DepartmentId = Dept, CallId = CallId, Status = (int)IncidentCommandStatus.Active
			});
			// A row with that id exists but belongs to another department — reject, never take it over.
			_nodeRepo.Setup(x => x.GetByIdAsync(clientId)).ReturnsAsync(new CommandStructureNode
			{
				CommandStructureNodeId = clientId, DepartmentId = 99, CallId = CallId
			});

			var node = new CommandStructureNode { CommandStructureNodeId = clientId, IncidentCommandId = "ic1", DepartmentId = Dept, Name = "Staging" };
			var saved = await _service.SaveNodeAsync(node, "user1");

			saved.Should().BeNull();
			_nodeRepo.Verify(x => x.InsertAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			_nodeRepo.Verify(x => x.SaveOrUpdateAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task DeleteNodeAsync_SoftDeletes_SetsDeletedOnAndModifiedOn_AndPersistsInsteadOfHardDeleting()
		{
			CommandStructureNode persisted = null;
			_nodeRepo.Setup(x => x.GetByIdAsync("node-1")).ReturnsAsync(new CommandStructureNode
			{
				CommandStructureNodeId = "node-1", DepartmentId = Dept, CallId = CallId, IncidentCommandId = "ic1", Name = "Staging"
			});
			// A hard delete would never call SaveOrUpdate; capturing it here proves the removal is a soft-delete.
			_nodeRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((CommandStructureNode n, CancellationToken ct, bool b) => persisted = n)
				.ReturnsAsync((CommandStructureNode n, CancellationToken ct, bool b) => n);

			var result = await _service.DeleteNodeAsync(Dept, "node-1", "user1");

			result.Should().BeTrue();
			persisted.Should().NotBeNull();
			persisted.DeletedOn.Should().NotBeNull();
			persisted.ModifiedOn.Should().NotBeNull();
		}

		[Test]
		public async Task GetNodesForCallAsync_ExcludesSoftDeletedNodes()
		{
			_nodeRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<CommandStructureNode>
			{
				new CommandStructureNode { CommandStructureNodeId = "n1", DepartmentId = Dept, CallId = CallId, Name = "Live", SortOrder = 1 },
				new CommandStructureNode { CommandStructureNodeId = "n2", DepartmentId = Dept, CallId = CallId, Name = "Removed", SortOrder = 2, DeletedOn = DateTime.UtcNow }
			});

			var nodes = await _service.GetNodesForCallAsync(Dept, CallId);

			nodes.Should().ContainSingle().Which.CommandStructureNodeId.Should().Be("n1");
		}

		// Delta pull (offline reconnect): returns only rows changed after the cursor — INCLUDING soft-deleted rows so
		// the client can remove them locally; rows with no ModifiedOn or an older ModifiedOn are excluded.

		[Test]
		public async Task GetChangesSinceAsync_ReturnsOnlyRowsChangedAfterCursor_IncludingSoftDeleted()
		{
			var since = DateTime.UtcNow.AddMinutes(-10);

			_commandRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentCommand>
			{
				new IncidentCommand { IncidentCommandId = "c-new", DepartmentId = Dept, CallId = CallId, ModifiedOn = DateTime.UtcNow },
				new IncidentCommand { IncidentCommandId = "c-old", DepartmentId = Dept, CallId = CallId, ModifiedOn = since.AddMinutes(-5) },
				new IncidentCommand { IncidentCommandId = "c-null", DepartmentId = Dept, CallId = CallId, ModifiedOn = null }
			});
			_nodeRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<CommandStructureNode>
			{
				// A lane soft-deleted after the cursor must be INCLUDED (with DeletedOn) so the client removes it.
				new CommandStructureNode { CommandStructureNodeId = "n-del", DepartmentId = Dept, CallId = CallId, DeletedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow }
			});

			var changes = await _service.GetChangesSinceAsync(Dept, since);

			changes.ServerTimestampMs.Should().BeGreaterThan(0);
			changes.Commands.Should().ContainSingle().Which.IncidentCommandId.Should().Be("c-new");
			changes.Nodes.Should().ContainSingle().Which.DeletedOn.Should().NotBeNull();
		}
	}
}
