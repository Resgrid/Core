using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Incident needs, objective progress, lane leads (change detection + notification fan-out), command
	/// details, and the resource-facing incident view.
	/// </summary>
	[TestFixture]
	public class IncidentCommandNeedsAndLeadsTests
	{
		private const int Dept = 44;
		private const int CallId = 900;
		private const string CommandId = "ic-1";

		private Mock<IIncidentCommandRepository> _commandRepo;
		private Mock<ICommandStructureNodeRepository> _nodeRepo;
		private Mock<IResourceAssignmentRepository> _assignmentRepo;
		private Mock<ITacticalObjectiveRepository> _objectiveRepo;
		private Mock<ICommandLogEntryRepository> _logRepo;
		private Mock<IIncidentNeedRepository> _needRepo;
		private Mock<IIncidentNeedUpdateRepository> _needUpdateRepo;
		private Mock<IIncidentNoteRepository> _noteRepo;
		private Mock<IIncidentAttachmentRepository> _attachmentRepo;
		private Mock<IUserProfileService> _userProfileService;
		private Mock<IIncidentCommandNotificationService> _notificationService;
		private Mock<IEventAggregator> _eventAggregator;
		private IncidentCommandService _service;

		[SetUp]
		public void SetUp()
		{
			_commandRepo = new Mock<IIncidentCommandRepository>();
			_nodeRepo = new Mock<ICommandStructureNodeRepository>();
			_assignmentRepo = new Mock<IResourceAssignmentRepository>();
			_objectiveRepo = new Mock<ITacticalObjectiveRepository>();
			_logRepo = new Mock<ICommandLogEntryRepository>();
			_needRepo = new Mock<IIncidentNeedRepository>();
			_needUpdateRepo = new Mock<IIncidentNeedUpdateRepository>();
			_needUpdateRepo.Setup(x => x.InsertAsync(It.IsAny<IncidentNeedUpdate>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentNeedUpdate e, CancellationToken ct, bool b) => e);
			_noteRepo = new Mock<IIncidentNoteRepository>();
			_attachmentRepo = new Mock<IIncidentAttachmentRepository>();
			_userProfileService = new Mock<IUserProfileService>();
			_notificationService = new Mock<IIncidentCommandNotificationService>();
			_eventAggregator = new Mock<IEventAggregator>();

			_logRepo.Setup(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CommandLogEntry e, CancellationToken ct, bool b) => e);
			_needRepo.Setup(x => x.InsertAsync(It.IsAny<IncidentNeed>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentNeed e, CancellationToken ct, bool b) => e);
			_needRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IncidentNeed>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentNeed e, CancellationToken ct, bool b) => e);
			_nodeRepo.Setup(x => x.InsertAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CommandStructureNode e, CancellationToken ct, bool b) => e);
			_nodeRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommandStructureNode>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CommandStructureNode e, CancellationToken ct, bool b) => e);
			_objectiveRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<TacticalObjective>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((TacticalObjective e, CancellationToken ct, bool b) => e);

			_commandRepo.Setup(x => x.GetByIdAsync(CommandId))
				.ReturnsAsync(OwnedCommand());
			_commandRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IncidentCommand>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentCommand e, CancellationToken ct, bool b) => e);

			_service = new IncidentCommandService(_commandRepo.Object, _nodeRepo.Object, _assignmentRepo.Object,
				_objectiveRepo.Object, new Mock<IIncidentTimerRepository>().Object, new Mock<IIncidentMapAnnotationRepository>().Object,
				_logRepo.Object, new Mock<ICommandTransferRepository>().Object,
				new Mock<ICommandsService>().Object, new Mock<ICallsService>().Object, new Mock<ICheckInTimerService>().Object,
				new Mock<IIncidentVoiceService>().Object, new Mock<IIncidentRoleAssignmentRepository>().Object,
				_eventAggregator.Object, new Mock<ICoreEventService>().Object,
				new Mock<IUnitsService>().Object, new Mock<IPersonnelRolesService>().Object, _noteRepo.Object,
				_attachmentRepo.Object, new Mock<IIncidentWeatherProvider>().Object,
				_needRepo.Object, _userProfileService.Object, _notificationService.Object,
				new Mock<IGeoLocationProvider>().Object, new Mock<IDepartmentGroupsService>().Object,
				new Mock<IIncidentAdHocUnitRepository>().Object, new Mock<IIncidentAdHocPersonnelRepository>().Object,
				_needUpdateRepo.Object,
				new Mock<IIncidentMapRepository>().Object,
				new Mock<IIncidentNeedEntityRepository>().Object, new Mock<ICallDispatchStatusService>().Object, new Mock<IQueueService>().Object);
		}

		private static IncidentCommand OwnedCommand() => new IncidentCommand
		{
			IncidentCommandId = CommandId,
			DepartmentId = Dept,
			CallId = CallId,
			Status = (int)IncidentCommandStatus.Active,
			EstablishedOn = DateTime.UtcNow.AddHours(-1),
			CurrentCommanderUserId = "cmdr-1",
			EstimatedEndOn = DateTime.UtcNow.AddHours(3),
			ImportantInformation = "Watch the north wall"
		};

		[Test]
		public async Task SaveNeed_New_StampsOwnershipWritesLogAndRaisesEvent()
		{
			var need = new IncidentNeed { IncidentCommandId = CommandId, DepartmentId = Dept, Name = "Fuel truck", QuantityRequested = 2 };

			var saved = await _service.SaveNeedAsync(need, "user-1");

			saved.Should().NotBeNull();
			saved.CallId.Should().Be(CallId, "the authoritative CallId comes from the parent command");
			saved.CreatedByUserId.Should().Be("user-1");
			saved.IncidentNeedId.Should().NotBeNullOrEmpty();
			saved.ModifiedOn.Should().NotBeNull();

			_logRepo.Verify(x => x.InsertAsync(It.Is<CommandLogEntry>(e => e.EntryType == (int)CommandLogEntryType.NeedAdded), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			_eventAggregator.Verify(x => x.SendMessage<Resgrid.Model.Events.IncidentNeedChangedEvent>(It.IsAny<Resgrid.Model.Events.IncidentNeedChangedEvent>()), Times.Once);
		}

		[Test]
		public async Task SetNeedStatus_Met_StampsMetByAndDefaultsFulfilledQuantity()
		{
			_needRepo.Setup(x => x.GetByIdAsync("need-1")).ReturnsAsync(new IncidentNeed
			{
				IncidentNeedId = "need-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Fuel truck",
				QuantityRequested = 2,
				Status = (int)IncidentNeedStatus.Open
			});

			var met = await _service.SetNeedStatusAsync(Dept, "need-1", IncidentNeedStatus.Met, null, "user-2");

			met.Status.Should().Be((int)IncidentNeedStatus.Met);
			met.MetByUserId.Should().Be("user-2");
			met.MetOn.Should().NotBeNull();
			met.QuantityFulfilled.Should().Be(2, "meeting an unquantified transition fills the requested quantity");

			_logRepo.Verify(x => x.InsertAsync(It.Is<CommandLogEntry>(e => e.EntryType == (int)CommandLogEntryType.NeedMet), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task SetNeedStatus_PartialFillWithNote_WritesAuditRowAndNotedLogEntry()
		{
			_needRepo.Setup(x => x.GetByIdAsync("need-1")).ReturnsAsync(new IncidentNeed
			{
				IncidentNeedId = "need-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Engines",
				QuantityRequested = 3,
				QuantityFulfilled = 0,
				Status = (int)IncidentNeedStatus.Open
			});

			IncidentNeedUpdate audit = null;
			_needUpdateRepo.Setup(x => x.InsertAsync(It.IsAny<IncidentNeedUpdate>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((IncidentNeedUpdate e, CancellationToken ct, bool b) => audit = e)
				.ReturnsAsync((IncidentNeedUpdate e, CancellationToken ct, bool b) => e);

			CommandLogEntry logged = null;
			_logRepo.Setup(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((CommandLogEntry e, CancellationToken ct, bool b) => logged = e)
				.ReturnsAsync((CommandLogEntry e, CancellationToken ct, bool b) => e);

			var updated = await _service.SetNeedStatusAsync(Dept, "need-1", IncidentNeedStatus.PartiallyMet, 1, "user-2", "Engine 1 from mutual aid");

			updated.Status.Should().Be((int)IncidentNeedStatus.PartiallyMet);
			updated.QuantityFulfilled.Should().Be(1);

			audit.Should().NotBeNull();
			audit.PreviousStatus.Should().Be((int)IncidentNeedStatus.Open);
			audit.NewStatus.Should().Be((int)IncidentNeedStatus.PartiallyMet);
			audit.PreviousQuantityFulfilled.Should().Be(0);
			audit.NewQuantityFulfilled.Should().Be(1);
			audit.Note.Should().Be("Engine 1 from mutual aid");
			audit.CreatedByUserId.Should().Be("user-2");
			audit.CreatedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));

			logged.Description.Should().Contain("fill changed 0 to 1 of 3");
			logged.Description.Should().Contain("Engine 1 from mutual aid");
		}

		[Test]
		public async Task SetNeedStatus_ReducingFill_IsAllowedAndAudited()
		{
			_needRepo.Setup(x => x.GetByIdAsync("need-1")).ReturnsAsync(new IncidentNeed
			{
				IncidentNeedId = "need-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Engines",
				QuantityRequested = 3,
				QuantityFulfilled = 2,
				Status = (int)IncidentNeedStatus.PartiallyMet
			});

			var updated = await _service.SetNeedStatusAsync(Dept, "need-1", IncidentNeedStatus.PartiallyMet, 1, "user-2", "Engine 1 called off to another incident");

			updated.QuantityFulfilled.Should().Be(1);
			_needUpdateRepo.Verify(x => x.InsertAsync(It.Is<IncidentNeedUpdate>(e =>
				e.PreviousQuantityFulfilled == 2 && e.NewQuantityFulfilled == 1 && e.Note == "Engine 1 called off to another incident"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task SetNeedStatus_CancelledUnfilled_LogsClosedWithoutBeingFilled()
		{
			_needRepo.Setup(x => x.GetByIdAsync("need-1")).ReturnsAsync(new IncidentNeed
			{
				IncidentNeedId = "need-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Engines",
				QuantityRequested = 3,
				QuantityFulfilled = 1,
				Status = (int)IncidentNeedStatus.PartiallyMet
			});

			CommandLogEntry logged = null;
			_logRepo.Setup(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((CommandLogEntry e, CancellationToken ct, bool b) => logged = e)
				.ReturnsAsync((CommandLogEntry e, CancellationToken ct, bool b) => e);

			var updated = await _service.SetNeedStatusAsync(Dept, "need-1", IncidentNeedStatus.Cancelled, null, "user-2", "No longer needed");

			updated.Status.Should().Be((int)IncidentNeedStatus.Cancelled);
			logged.Description.Should().Contain("closed without being filled");
			logged.Description.Should().Contain("No longer needed");
		}

		[Test]
		public async Task GetNeedUpdates_ReturnsNewestFirst_WithResolvedAuthorNames()
		{
			_needUpdateRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentNeedUpdate>
			{
				new IncidentNeedUpdate { IncidentNeedUpdateId = "u1", IncidentNeedId = "need-1", DepartmentId = Dept, CreatedByUserId = "user-2", CreatedOn = DateTime.UtcNow.AddMinutes(-10) },
				new IncidentNeedUpdate { IncidentNeedUpdateId = "u2", IncidentNeedId = "need-1", DepartmentId = Dept, CreatedByUserId = "user-2", CreatedOn = DateTime.UtcNow },
				new IncidentNeedUpdate { IncidentNeedUpdateId = "other", IncidentNeedId = "need-9", DepartmentId = Dept, CreatedByUserId = "user-2", CreatedOn = DateTime.UtcNow }
			});

			var updates = await _service.GetNeedUpdatesAsync(Dept, "need-1");

			updates.Select(x => x.IncidentNeedUpdateId).Should().Equal("u2", "u1");
		}

		[Test]
		public async Task RequestNeedEntities_CreatesNeed_DispatchesSubset_AndLogsRequest()
		{
			var entityRepo = new Mock<IIncidentNeedEntityRepository>();
			entityRepo.Setup(x => x.InsertAsync(It.IsAny<IncidentNeedEntity>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentNeedEntity e, CancellationToken ct, bool b) => e);
			var dispatchStatus = new Mock<ICallDispatchStatusService>();
			var queue = new Mock<IQueueService>();
			CallQueueItem enqueued = null;
			queue.Setup(x => x.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.Callback((CallQueueItem cqi, CancellationToken ct) => enqueued = cqi)
				.ReturnsAsync(true);

			var callsService = new Mock<ICallsService>();
			var call = new Call { CallId = CallId, DepartmentId = Dept, Name = "Structure Fire", NatureOfCall = "Working fire" };
			callsService.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(call);
			callsService
				.Setup(x => x.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call c, bool a1, bool a2, bool a3, bool a4, bool a5, bool a6, bool a7, bool a8, bool a9, bool a10) => c);
			callsService.Setup(x => x.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>())).ReturnsAsync((Call c, CancellationToken ct) => c);

			var service = new IncidentCommandService(_commandRepo.Object, _nodeRepo.Object, _assignmentRepo.Object,
				_objectiveRepo.Object, new Mock<IIncidentTimerRepository>().Object, new Mock<IIncidentMapAnnotationRepository>().Object,
				_logRepo.Object, new Mock<ICommandTransferRepository>().Object,
				new Mock<ICommandsService>().Object, callsService.Object, new Mock<ICheckInTimerService>().Object,
				new Mock<IIncidentVoiceService>().Object, new Mock<IIncidentRoleAssignmentRepository>().Object,
				_eventAggregator.Object, new Mock<ICoreEventService>().Object,
				new Mock<IUnitsService>().Object, new Mock<IPersonnelRolesService>().Object, _noteRepo.Object,
				_attachmentRepo.Object, new Mock<IIncidentWeatherProvider>().Object,
				_needRepo.Object, _userProfileService.Object, _notificationService.Object,
				new Mock<IGeoLocationProvider>().Object, new Mock<IDepartmentGroupsService>().Object,
				new Mock<IIncidentAdHocUnitRepository>().Object, new Mock<IIncidentAdHocPersonnelRepository>().Object,
				_needUpdateRepo.Object, new Mock<IIncidentMapRepository>().Object,
				entityRepo.Object, dispatchStatus.Object, queue.Object);

			CommandLogEntry logged = null;
			_logRepo.Setup(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((CommandLogEntry e, CancellationToken ct, bool b) => logged = e)
				.ReturnsAsync((CommandLogEntry e, CancellationToken ct, bool b) => e);

			var entities = new List<IncidentNeedEntity>
			{
				new IncidentNeedEntity { EntityKind = (int)NeedEntityKind.Unit, EntityId = "5" },
				new IncidentNeedEntity { EntityKind = (int)NeedEntityKind.User, EntityId = "user-9" }
			};

			var need = await service.RequestNeedEntitiesAsync(Dept, CommandId, "Mutual aid engines", "Need two engines", entities, "user-2");

			need.Should().NotBeNull();
			need.Category.Should().Be((int)IncidentNeedCategory.Entity);
			need.QuantityRequested.Should().Be(2);

			// Subset dispatch was enqueued (only the requested entities), tagged as a command request.
			enqueued.Should().NotBeNull();
			enqueued.BroadcastOnlySelectedDispatches.Should().BeTrue();
			enqueued.BroadcastUnitIds.Should().Contain(5);
			enqueued.BroadcastUserIds.Should().Contain("user-9");
			enqueued.Call.NatureOfCall.Should().Contain("Requested by Incident Command");

			// Entity rows persisted with dispatch stamps, and the request landed on the log.
			entityRepo.Verify(x => x.InsertAsync(It.Is<IncidentNeedEntity>(e => e.IncidentNeedId == need.IncidentNeedId && e.DispatchedOn != null), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
			logged.EntryType.Should().Be((int)CommandLogEntryType.NeedAdded);
			logged.Description.Should().Contain("Mutual aid engines");
			logged.Description.Should().Contain("dispatched to the call individually");
		}

		[Test]
		public async Task RecordNeedEntityStatus_MatchingUnit_WritesResponseLogEntry()
		{
			var entityRepo = new Mock<IIncidentNeedEntityRepository>();
			entityRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentNeedEntity>
			{
				new IncidentNeedEntity { IncidentNeedEntityId = "ne-1", IncidentNeedId = "need-1", IncidentCommandId = CommandId, DepartmentId = Dept, CallId = CallId, EntityKind = (int)NeedEntityKind.Unit, EntityId = "5" }
			});
			_needRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentNeed>
			{
				new IncidentNeed { IncidentNeedId = "need-1", DepartmentId = Dept, CallId = CallId, Name = "Mutual aid engines" }
			});
			_commandRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentCommand> { OwnedCommand() });

			var service = new IncidentCommandService(_commandRepo.Object, _nodeRepo.Object, _assignmentRepo.Object,
				_objectiveRepo.Object, new Mock<IIncidentTimerRepository>().Object, new Mock<IIncidentMapAnnotationRepository>().Object,
				_logRepo.Object, new Mock<ICommandTransferRepository>().Object,
				new Mock<ICommandsService>().Object, new Mock<ICallsService>().Object, new Mock<ICheckInTimerService>().Object,
				new Mock<IIncidentVoiceService>().Object, new Mock<IIncidentRoleAssignmentRepository>().Object,
				_eventAggregator.Object, new Mock<ICoreEventService>().Object,
				new Mock<IUnitsService>().Object, new Mock<IPersonnelRolesService>().Object, _noteRepo.Object,
				_attachmentRepo.Object, new Mock<IIncidentWeatherProvider>().Object,
				_needRepo.Object, _userProfileService.Object, _notificationService.Object,
				new Mock<IGeoLocationProvider>().Object, new Mock<IDepartmentGroupsService>().Object,
				new Mock<IIncidentAdHocUnitRepository>().Object, new Mock<IIncidentAdHocPersonnelRepository>().Object,
				_needUpdateRepo.Object, new Mock<IIncidentMapRepository>().Object,
				entityRepo.Object, new Mock<ICallDispatchStatusService>().Object, new Mock<IQueueService>().Object);

			CommandLogEntry logged = null;
			_logRepo.Setup(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((CommandLogEntry e, CancellationToken ct, bool b) => logged = e)
				.ReturnsAsync((CommandLogEntry e, CancellationToken ct, bool b) => e);

			await service.RecordNeedEntityStatusAsync(Dept, CallId, NeedEntityKind.Unit, "5", "Responding", "user-9");

			logged.Should().NotBeNull();
			logged.EntryType.Should().Be((int)CommandLogEntryType.NeedUpdated);
			logged.Description.Should().Contain("responded to need 'Mutual aid engines'");
			logged.Description.Should().Contain("Responding");
		}

		[Test]
		public async Task SaveIncidentMap_New_StampsAuditAndLogsAttributedEntry()
		{
			var mapRepo = new Mock<IIncidentMapRepository>();
			mapRepo.Setup(x => x.InsertAsync(It.IsAny<IncidentMap>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentMap e, CancellationToken ct, bool b) => e);
			mapRepo.Setup(x => x.SaveOrUpdateAsync(It.IsAny<IncidentMap>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((IncidentMap e, CancellationToken ct, bool b) => e);

			var service = new IncidentCommandService(_commandRepo.Object, _nodeRepo.Object, _assignmentRepo.Object,
				_objectiveRepo.Object, new Mock<IIncidentTimerRepository>().Object, new Mock<IIncidentMapAnnotationRepository>().Object,
				_logRepo.Object, new Mock<ICommandTransferRepository>().Object,
				new Mock<ICommandsService>().Object, new Mock<ICallsService>().Object, new Mock<ICheckInTimerService>().Object,
				new Mock<IIncidentVoiceService>().Object, new Mock<IIncidentRoleAssignmentRepository>().Object,
				_eventAggregator.Object, new Mock<ICoreEventService>().Object,
				new Mock<IUnitsService>().Object, new Mock<IPersonnelRolesService>().Object, _noteRepo.Object,
				_attachmentRepo.Object, new Mock<IIncidentWeatherProvider>().Object,
				_needRepo.Object, _userProfileService.Object, _notificationService.Object,
				new Mock<IGeoLocationProvider>().Object, new Mock<IDepartmentGroupsService>().Object,
				new Mock<IIncidentAdHocUnitRepository>().Object, new Mock<IIncidentAdHocPersonnelRepository>().Object,
				_needUpdateRepo.Object, mapRepo.Object,
				new Mock<IIncidentNeedEntityRepository>().Object, new Mock<ICallDispatchStatusService>().Object, new Mock<IQueueService>().Object);

			CommandLogEntry logged = null;
			_logRepo.Setup(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((CommandLogEntry e, CancellationToken ct, bool b) => logged = e)
				.ReturnsAsync((CommandLogEntry e, CancellationToken ct, bool b) => e);

			var saved = await service.SaveIncidentMapAsync(new IncidentMap
			{
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				Name = "North sector cleanup",
				Description = "Debris removal area",
				ExpiresOn = DateTime.UtcNow.AddDays(2)
			}, "user-2");

			saved.Should().NotBeNull();
			saved.CallId.Should().Be(CallId, "the authoritative CallId comes from the parent command");
			saved.IncidentMapId.Should().NotBeNullOrEmpty();
			saved.CreatedByUserId.Should().Be("user-2");
			saved.CreatedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));

			logged.EntryType.Should().Be((int)CommandLogEntryType.MapViewUpdated);
			logged.Description.Should().Contain("North sector cleanup");
			logged.Description.Should().Contain("created");
		}

		[Test]
		public async Task SetNeedStatus_ForeignDepartment_ReturnsNull()
		{
			_needRepo.Setup(x => x.GetByIdAsync("need-1")).ReturnsAsync(new IncidentNeed { IncidentNeedId = "need-1", DepartmentId = Dept + 1 });

			var result = await _service.SetNeedStatusAsync(Dept, "need-1", IncidentNeedStatus.Met, null, "user-2");

			result.Should().BeNull();
		}

		[Test]
		public async Task CompleteObjective_WithOutcomeAndNote_StampsAuditAndLogsOutcome()
		{
			_objectiveRepo.Setup(x => x.GetByIdAsync("obj-1")).ReturnsAsync(new TacticalObjective
			{
				TacticalObjectiveId = "obj-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Primary search",
				Status = (int)TacticalObjectiveStatus.InProgress,
				ProgressPercent = 60
			});

			CommandLogEntry logged = null;
			_logRepo.Setup(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((CommandLogEntry e, CancellationToken ct, bool b) => logged = e)
				.ReturnsAsync((CommandLogEntry e, CancellationToken ct, bool b) => e);

			var completed = await _service.CompleteObjectiveAsync(Dept, "obj-1", "user-2", TacticalObjectiveOutcome.Partial, "North wing cleared; south wing inaccessible");

			completed.Status.Should().Be((int)TacticalObjectiveStatus.Complete);
			completed.Outcome.Should().Be((int)TacticalObjectiveOutcome.Partial);
			completed.CompletionNote.Should().Be("North wing cleared; south wing inaccessible");
			completed.CompletedByUserId.Should().Be("user-2");
			completed.CompletedOn.Should().NotBeNull();

			logged.EntryType.Should().Be((int)CommandLogEntryType.ObjectiveCompleted);
			logged.Description.Should().Contain("Partial");
			logged.Description.Should().Contain("North wing cleared; south wing inaccessible");
		}

		[Test]
		public async Task UpdateObjectiveProgress_ReachingFull_ClosesOutAsSuccessful()
		{
			_objectiveRepo.Setup(x => x.GetByIdAsync("obj-1")).ReturnsAsync(new TacticalObjective
			{
				TacticalObjectiveId = "obj-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Primary search",
				Status = (int)TacticalObjectiveStatus.InProgress,
				ProgressPercent = 75
			});

			var completed = await _service.UpdateObjectiveProgressAsync(Dept, "obj-1", 100, "user-2");

			completed.Status.Should().Be((int)TacticalObjectiveStatus.Complete);
			completed.Outcome.Should().Be((int)TacticalObjectiveOutcome.Successful);
		}

		[Test]
		public async Task UpdateObjectiveProgress_PartialProgress_MovesPendingToInProgress()
		{
			_objectiveRepo.Setup(x => x.GetByIdAsync("obj-1")).ReturnsAsync(new TacticalObjective
			{
				TacticalObjectiveId = "obj-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Primary search",
				Status = (int)TacticalObjectiveStatus.Pending
			});

			var updated = await _service.UpdateObjectiveProgressAsync(Dept, "obj-1", 40, "user-1");

			updated.ProgressPercent.Should().Be(40);
			updated.Status.Should().Be((int)TacticalObjectiveStatus.InProgress);
			_logRepo.Verify(x => x.InsertAsync(It.Is<CommandLogEntry>(e => e.EntryType == (int)CommandLogEntryType.ObjectiveProgressUpdated), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task UpdateObjectiveProgress_OneHundredPercent_CompletesTheObjective()
		{
			_objectiveRepo.Setup(x => x.GetByIdAsync("obj-1")).ReturnsAsync(new TacticalObjective
			{
				TacticalObjectiveId = "obj-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Primary search",
				Status = (int)TacticalObjectiveStatus.InProgress,
				ProgressPercent = 80
			});

			var updated = await _service.UpdateObjectiveProgressAsync(Dept, "obj-1", 150, "user-1");

			updated.Status.Should().Be((int)TacticalObjectiveStatus.Complete, "progress is clamped to 100 which completes");
			updated.ProgressPercent.Should().Be(100);
			updated.CompletedByUserId.Should().Be("user-1");
			updated.CompletedOn.Should().NotBeNull();
		}

		[Test]
		public async Task SaveNode_LeadChange_NotifiesIncidentAndWritesTimeline()
		{
			var stored = new CommandStructureNode
			{
				CommandStructureNodeId = "node-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Medical",
				PrimaryLeadUserId = "old-lead"
			};
			_nodeRepo.Setup(x => x.GetByIdAsync("node-1")).ReturnsAsync(stored);

			var incoming = new CommandStructureNode
			{
				CommandStructureNodeId = "node-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Medical",
				PrimaryLeadUserId = "new-lead"
			};

			var saved = await _service.SaveNodeAsync(incoming, "user-1");

			saved.Should().NotBeNull();
			_notificationService.Verify(x => x.NotifyLaneLeadChangedAsync(Dept, CallId, "Medical", true,
				"old-lead", null, "new-lead", null, It.IsAny<CancellationToken>()), Times.Once);
			_notificationService.Verify(x => x.NotifyLaneLeadChangedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), false,
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_logRepo.Verify(x => x.InsertAsync(It.Is<CommandLogEntry>(e => e.EntryType == (int)CommandLogEntryType.LaneLeadChanged), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task SaveNode_NoLeadChange_DoesNotNotify()
		{
			var stored = new CommandStructureNode
			{
				CommandStructureNodeId = "node-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Medical",
				PrimaryLeadUserId = "lead-1",
				SecondaryLeadName = "Jane External"
			};
			_nodeRepo.Setup(x => x.GetByIdAsync("node-1")).ReturnsAsync(stored);

			var incoming = new CommandStructureNode
			{
				CommandStructureNodeId = "node-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Medical",
				PrimaryLeadUserId = "lead-1",
				SecondaryLeadName = "Jane External"
			};

			await _service.SaveNodeAsync(incoming, "user-1");

			_notificationService.Verify(x => x.NotifyLaneLeadChangedAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task GetResourceIncidentView_ForAssignedUser_ResolvesLaneLeadsAndObjectives()
		{
			var objective = new TacticalObjective { TacticalObjectiveId = "obj-1", DepartmentId = Dept, CallId = CallId, Name = "Primary search", ProgressPercent = 25 };
			var need = new IncidentNeed { IncidentNeedId = "need-1", DepartmentId = Dept, CallId = CallId, Name = "Fuel truck" };
			var node = new CommandStructureNode
			{
				CommandStructureNodeId = "node-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Medical",
				NodeType = (int)CommandNodeType.Group,
				Color = "#3498db",
				PrimaryObjectiveId = "obj-1",
				LinkedNeedId = "need-1",
				PrimaryLeadUserId = "lead-1",
				SecondaryLeadName = "Jane External",
				SecondaryLeadPhone = "555-0100"
			};

			_commandRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentCommand> { OwnedCommand() });
			_objectiveRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<TacticalObjective> { objective });
			_needRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentNeed> { need });
			_noteRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentNote>());
			_attachmentRepo.Setup(x => x.GetAllMetadataByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentAttachment>());
			_assignmentRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<ResourceAssignment>
			{
				new ResourceAssignment
				{
					ResourceAssignmentId = "ra-1",
					IncidentCommandId = CommandId,
					DepartmentId = Dept,
					CallId = CallId,
					CommandStructureNodeId = "node-1",
					ResourceKind = (int)ResourceAssignmentKind.RealPersonnel,
					ResourceId = "user-9",
					AssignedOn = DateTime.UtcNow.AddMinutes(-10)
				}
			});
			_nodeRepo.Setup(x => x.GetByIdAsync("node-1")).ReturnsAsync(node);
			_userProfileService.Setup(x => x.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((string userId, bool bypass) => new UserProfile { UserId = userId, FirstName = "First", LastName = userId, MobileNumber = "555-0199" });

			var view = await _service.GetResourceIncidentViewAsync(Dept, CallId, "user-9", null, includePrivate: false);

			view.Should().NotBeNull();
			view.Commander.Should().NotBeNull();
			view.Commander.UserId.Should().Be("cmdr-1");
			view.ImportantInformation.Should().Be("Watch the north wall");
			view.EstimatedEndOn.Should().NotBeNull();
			view.Objectives.Should().ContainSingle(o => o.TacticalObjectiveId == "obj-1");
			view.Needs.Should().ContainSingle(n => n.IncidentNeedId == "need-1");

			view.MyAssignment.Should().NotBeNull();
			view.MyAssignment.LaneName.Should().Be("Medical");
			view.MyAssignment.PrimaryLead.Should().NotBeNull();
			view.MyAssignment.PrimaryLead.UserId.Should().Be("lead-1");
			view.MyAssignment.SecondaryLead.Should().NotBeNull();
			view.MyAssignment.SecondaryLead.Name.Should().Be("Jane External");
			view.MyAssignment.SecondaryLead.Phone.Should().Be("555-0100");
			view.MyAssignment.PrimaryObjective.Should().NotBeNull();
			view.MyAssignment.PrimaryObjective.TacticalObjectiveId.Should().Be("obj-1");
			view.MyAssignment.LinkedNeed.Should().NotBeNull();
			view.MyAssignment.LinkedNeed.IncidentNeedId.Should().Be("need-1");
		}

		[Test]
		public async Task GetResourceIncidentView_ForUnit_ResolvesUnitAssignment()
		{
			_commandRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentCommand> { OwnedCommand() });
			_objectiveRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<TacticalObjective>());
			_needRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentNeed>());
			_noteRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentNote>());
			_attachmentRepo.Setup(x => x.GetAllMetadataByDepartmentIdAsync(Dept)).ReturnsAsync(new List<IncidentAttachment>());
			_assignmentRepo.Setup(x => x.GetAllByDepartmentIdAsync(Dept)).ReturnsAsync(new List<ResourceAssignment>
			{
				new ResourceAssignment
				{
					ResourceAssignmentId = "ra-2",
					IncidentCommandId = CommandId,
					DepartmentId = Dept,
					CallId = CallId,
					CommandStructureNodeId = "node-1",
					ResourceKind = (int)ResourceAssignmentKind.RealUnit,
					ResourceId = "77",
					AssignedOn = DateTime.UtcNow
				}
			});
			_nodeRepo.Setup(x => x.GetByIdAsync("node-1")).ReturnsAsync(new CommandStructureNode
			{
				CommandStructureNodeId = "node-1",
				IncidentCommandId = CommandId,
				DepartmentId = Dept,
				CallId = CallId,
				Name = "Staging"
			});

			var view = await _service.GetResourceIncidentViewAsync(Dept, CallId, "someone-else", 77, includePrivate: false);

			view.MyAssignment.Should().NotBeNull();
			view.MyAssignment.LaneName.Should().Be("Staging");
		}

		[Test]
		public async Task UpdateCommandDetails_StampsFieldsAndWritesTimeline()
		{
			var estimatedEnd = DateTime.UtcNow.AddHours(6);

			var updated = await _service.UpdateCommandDetailsAsync(Dept, CommandId, estimatedEnd, "Stage on the east side", "user-1");

			updated.Should().NotBeNull();
			updated.EstimatedEndOn.Should().Be(estimatedEnd);
			updated.ImportantInformation.Should().Be("Stage on the east side");
			_logRepo.Verify(x => x.InsertAsync(It.Is<CommandLogEntry>(e => e.EntryType == (int)CommandLogEntryType.CommandDetailsUpdated), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}
	}
}
