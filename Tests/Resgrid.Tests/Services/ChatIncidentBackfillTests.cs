using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Incidents established before the incident chat channels existed heal themselves the first time
	/// someone opens the board or the responder view — no migration sweep. This fixture pins the parts
	/// that matter: only what is missing gets created, closed commands stay frozen, and a board that
	/// refreshes on a timer does not re-sweep.
	/// </summary>
	[TestFixture]
	public class ChatIncidentBackfillTests
	{
		private const int CallId = 42;
		private const string CommandId = "command-1";

		private Mock<IChatChannelRepository> _channelRepository;
		private Mock<ICacheProvider> _cacheProvider;
		private Mock<IChatPermissionService> _permissionService;
		private Mock<ICallsService> _callsService;
		private List<ChatChannel> _inserted;

		[SetUp]
		public void Setup()
		{
			_channelRepository = new Mock<IChatChannelRepository>();
			_cacheProvider = new Mock<ICacheProvider>();
			_permissionService = new Mock<IChatPermissionService>();
			_callsService = new Mock<ICallsService>();
			_inserted = new List<ChatChannel>();

			// No marker set: the backfill runs.
			_cacheProvider.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string)null);
			_cacheProvider.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);

			_channelRepository
				.Setup(x => x.InsertAsync(It.IsAny<ChatChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ChatChannel channel, CancellationToken _, bool __) =>
				{
					_inserted.Add(channel);
					return channel;
				});
		}

		private ChatChannelService BuildService()
			=> new ChatChannelService(
				_channelRepository.Object,
				Mock.Of<IChatChannelMemberRepository>(),
				Mock.Of<IChatChannelAccessRuleRepository>(),
				Mock.Of<IChatDepartmentSettingRepository>(),
				_permissionService.Object,
				Mock.Of<IDepartmentsService>(),
				Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IUnitsService>(),
				Mock.Of<IUserProfileService>(),
				_callsService.Object,
				Mock.Of<IEventAggregator>(),
				_cacheProvider.Object,
				Mock.Of<IUnitOfWork>(),
				Mock.Of<IIncidentCommandService>());

		private static IncidentCommand BuildCommand(IncidentCommandStatus status = IncidentCommandStatus.Active)
			=> new IncidentCommand
			{
				IncidentCommandId = CommandId,
				DepartmentId = 1,
				CallId = CallId,
				Status = (int)status
			};

		private static CommandStructureNode BuildNode(string id, bool deleted = false)
			=> new CommandStructureNode
			{
				CommandStructureNodeId = id,
				IncidentCommandId = CommandId,
				DepartmentId = 1,
				CallId = CallId,
				Name = id,
				DeletedOn = deleted ? DateTime.UtcNow : (DateTime?)null
			};

		private void GivenExistingChannels(params ChatChannel[] channels)
			=> _channelRepository.Setup(x => x.GetByCallIdAsync(CallId)).ReturnsAsync(new List<ChatChannel>(channels));

		[Test]
		public async Task an_incident_with_no_channels_gets_the_full_set()
		{
			GivenExistingChannels();

			await BuildService().EnsureIncidentChannelsAsync(BuildCommand(), new[] { BuildNode("node-1"), BuildNode("node-2") });

			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.Incident);
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentCommand);
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentLeads);
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentDispatch);
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentLane && c.CommandStructureNodeId == "node-1");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentLane && c.CommandStructureNodeId == "node-2");
		}

		[Test]
		public async Task only_the_missing_channels_are_created()
		{
			// An incident from after the command channel shipped but before "All Leads" did, with one of
			// its two lanes already provisioned.
			GivenExistingChannels(
				new ChatChannel { ChatChannelId = "a", CallId = CallId, ChannelType = (int)ChatChannelType.Incident },
				new ChatChannel { ChatChannelId = "b", CallId = CallId, ChannelType = (int)ChatChannelType.IncidentCommand },
				new ChatChannel { ChatChannelId = "c", CallId = CallId, ChannelType = (int)ChatChannelType.IncidentLane, CommandStructureNodeId = "node-1" });

			await BuildService().EnsureIncidentChannelsAsync(BuildCommand(), new[] { BuildNode("node-1"), BuildNode("node-2") });

			_inserted.Should().HaveCount(3);
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentLeads);
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentDispatch);
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentLane && c.CommandStructureNodeId == "node-2");
		}

		[Test]
		public async Task a_deleted_lane_does_not_get_a_channel()
		{
			GivenExistingChannels();

			await BuildService().EnsureIncidentChannelsAsync(BuildCommand(), new[] { BuildNode("node-1"), BuildNode("node-gone", deleted: true) });

			_inserted.Should().NotContain(c => c.CommandStructureNodeId == "node-gone");
		}

		[Test]
		public async Task a_closed_command_is_left_alone()
		{
			GivenExistingChannels();

			await BuildService().EnsureIncidentChannelsAsync(BuildCommand(IncidentCommandStatus.Closed), new[] { BuildNode("node-1") });

			// Creating a channel now would come back unarchived and quietly unfreeze a point-in-time record.
			_inserted.Should().BeEmpty();
			_channelRepository.Verify(x => x.GetByCallIdAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task a_recently_backfilled_incident_is_not_swept_again()
		{
			// The board is a polled read — without the marker every refresh would re-query the channels.
			_cacheProvider.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync("1");

			await BuildService().EnsureIncidentChannelsAsync(BuildCommand(), new[] { BuildNode("node-1") });

			_channelRepository.Verify(x => x.GetByCallIdAsync(It.IsAny<int>()), Times.Never);
			_inserted.Should().BeEmpty();
		}

		[Test]
		public async Task a_cache_outage_does_not_stop_the_backfill()
		{
			_cacheProvider.Setup(x => x.GetStringAsync(It.IsAny<string>())).ThrowsAsync(new Exception("cache down"));
			GivenExistingChannels();

			await BuildService().EnsureIncidentChannelsAsync(BuildCommand(), new[] { BuildNode("node-1") });

			_inserted.Should().NotBeEmpty();
		}

		[Test]
		public async Task a_repository_failure_never_reaches_the_caller()
		{
			// The board read must survive a chat problem — provisioning is supplementary to it.
			_channelRepository.Setup(x => x.GetByCallIdAsync(CallId)).ThrowsAsync(new Exception("db down"));

			var act = async () => await BuildService().EnsureIncidentChannelsAsync(BuildCommand(), new[] { BuildNode("node-1") });

			await act.Should().NotThrowAsync();
		}

		[Test]
		public async Task a_command_without_a_call_is_ignored()
		{
			var command = BuildCommand();
			command.CallId = 0;

			await BuildService().EnsureIncidentChannelsAsync(command, new[] { BuildNode("node-1") });

			_inserted.Should().BeEmpty();
		}

		[Test]
		public async Task channels_reused_from_a_prior_command_are_rebound_and_unarchived()
		{
			// Command #1 closed (its channels archived and still carrying its id), then command #2
			// established on the same call. The reused channels must come back to life under command #2.
			var archivedOn = DateTime.UtcNow.AddHours(-2);
			GivenExistingChannels(
				new ChatChannel { ChatChannelId = "a", CallId = CallId, ChannelType = (int)ChatChannelType.Incident },
				new ChatChannel { ChatChannelId = "b", CallId = CallId, ChannelType = (int)ChatChannelType.IncidentCommand, IncidentCommandId = "command-0", IsArchived = true, ArchivedOn = archivedOn },
				new ChatChannel { ChatChannelId = "c", CallId = CallId, ChannelType = (int)ChatChannelType.IncidentLeads, IncidentCommandId = "command-0", IsArchived = true, ArchivedOn = archivedOn },
				new ChatChannel { ChatChannelId = "d", CallId = CallId, ChannelType = (int)ChatChannelType.IncidentDispatch, IncidentCommandId = "command-0", IsArchived = true, ArchivedOn = archivedOn });

			await BuildService().EnsureIncidentChannelsAsync(BuildCommand(), new CommandStructureNode[0]);

			_inserted.Should().BeEmpty();
			_channelRepository.Verify(x => x.RebindToIncidentCommandAsync("b", CommandId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_channelRepository.Verify(x => x.RebindToIncidentCommandAsync("c", CommandId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_channelRepository.Verify(x => x.RebindToIncidentCommandAsync("d", CommandId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

			// Archived state gates posting through cached permission verdicts — stale entries must die now.
			// (AtLeastOnce: the rebind invalidates, and the naming refresh may invalidate again.)
			_permissionService.Verify(x => x.InvalidateChannelCacheAsync("b"), Times.AtLeastOnce);
			_permissionService.Verify(x => x.InvalidateChannelCacheAsync("c"), Times.AtLeastOnce);
			_permissionService.Verify(x => x.InvalidateChannelCacheAsync("d"), Times.AtLeastOnce);
		}

		[Test]
		public async Task channels_already_bound_to_the_active_command_are_not_rewritten()
		{
			// The steady state — same command, nothing archived, names current — must stay a pure read.
			GivenExistingChannels(
				new ChatChannel { ChatChannelId = "a", CallId = CallId, ChannelType = (int)ChatChannelType.Incident, Name = $"Call {CallId}" },
				new ChatChannel { ChatChannelId = "b", CallId = CallId, ChannelType = (int)ChatChannelType.IncidentCommand, IncidentCommandId = CommandId, Name = $"Call {CallId} Command (private)" },
				new ChatChannel { ChatChannelId = "c", CallId = CallId, ChannelType = (int)ChatChannelType.IncidentLeads, IncidentCommandId = CommandId, Name = $"Call {CallId} All Leads" },
				new ChatChannel { ChatChannelId = "d", CallId = CallId, ChannelType = (int)ChatChannelType.IncidentDispatch, IncidentCommandId = CommandId, Name = $"Call {CallId} Dispatch" });

			await BuildService().EnsureIncidentChannelsAsync(BuildCommand(), new CommandStructureNode[0]);

			_inserted.Should().BeEmpty();
			_channelRepository.Verify(x => x.RebindToIncidentCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			_channelRepository.Verify(x => x.UpdateChannelInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task channels_are_named_after_the_incident_when_the_command_has_a_name()
		{
			GivenExistingChannels();

			var command = BuildCommand();
			command.Name = "Barn Fire";

			await BuildService().EnsureIncidentChannelsAsync(command, new[] { BuildNode("Staging") });

			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.Incident && c.Name == "Barn Fire");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentCommand && c.Name == "Barn Fire Command (private)");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentLeads && c.Name == "Barn Fire All Leads");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentDispatch && c.Name == "Barn Fire Dispatch");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentLane && c.Name == "Barn Fire Staging");
		}

		[Test]
		public async Task channels_fall_back_to_the_call_name_when_the_command_is_unnamed()
		{
			GivenExistingChannels();
			_callsService.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = 1, Name = "Structure Fire" });

			await BuildService().EnsureIncidentChannelsAsync(BuildCommand(), new[] { BuildNode("Staging") });

			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.Incident && c.Name == "Structure Fire");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentCommand && c.Name == "Structure Fire Command (private)");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentLane && c.Name == "Structure Fire Staging");
		}

		[Test]
		public async Task the_call_number_prefixes_the_call_name_on_every_incident_channel()
		{
			// What a responder scanning the channel list actually recognizes: "26-45 Structure Fire",
			// not the call id.
			GivenExistingChannels();
			_callsService.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>()))
				.ReturnsAsync(new Call { CallId = CallId, DepartmentId = 1, Number = "26-45", Name = "Structure Fire" });

			await BuildService().EnsureIncidentChannelsAsync(BuildCommand(), new[] { BuildNode("Staging") });

			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.Incident && c.Name == "26-45 Structure Fire");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentCommand && c.Name == "26-45 Structure Fire Command (private)");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentLeads && c.Name == "26-45 Structure Fire All Leads");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentDispatch && c.Name == "26-45 Structure Fire Dispatch");
			_inserted.Should().Contain(c => c.ChannelType == (int)ChatChannelType.IncidentLane && c.Name == "26-45 Structure Fire Staging");
		}

		[Test]
		public async Task an_incident_channel_stuck_on_the_call_id_fallback_is_renamed()
		{
			// Channels created when the call lookup failed carry the bare "Call {id}" fallback forever —
			// the next ensure (call added, call edited) has to heal them in place.
			var existing = new ChatChannel { ChatChannelId = "a", CallId = CallId, ChannelType = (int)ChatChannelType.Incident, Name = $"Call {CallId}" };
			_channelRepository.Setup(x => x.GetByCallIdAndTypeAsync(CallId, (int)ChatChannelType.Incident)).ReturnsAsync(existing);

			await BuildService().EnsureIncidentChannelAsync(1, CallId, "26-45 Structure Fire");

			_inserted.Should().BeEmpty();
			_channelRepository.Verify(x => x.UpdateChannelInfoAsync("a", "26-45 Structure Fire", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_permissionService.Verify(x => x.InvalidateChannelCacheAsync("a"), Times.Once);
		}

		[Test]
		public async Task an_incident_channel_already_carrying_the_call_name_is_not_rewritten()
		{
			var existing = new ChatChannel { ChatChannelId = "a", CallId = CallId, ChannelType = (int)ChatChannelType.Incident, Name = "26-45 Structure Fire" };
			_channelRepository.Setup(x => x.GetByCallIdAndTypeAsync(CallId, (int)ChatChannelType.Incident)).ReturnsAsync(existing);

			await BuildService().EnsureIncidentChannelAsync(1, CallId, "26-45 Structure Fire");

			_channelRepository.Verify(x => x.UpdateChannelInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task stale_channel_names_are_refreshed_when_the_incident_is_named()
		{
			// Channels provisioned at call time (or by an old backfill) carry pre-incident names; naming
			// the command must flow into them without touching anything else.
			GivenExistingChannels(
				new ChatChannel { ChatChannelId = "a", CallId = CallId, ChannelType = (int)ChatChannelType.Incident, Name = "Call 42" },
				new ChatChannel { ChatChannelId = "b", CallId = CallId, ChannelType = (int)ChatChannelType.IncidentCommand, IncidentCommandId = CommandId, Name = "Command" },
				new ChatChannel { ChatChannelId = "c", CallId = CallId, ChannelType = (int)ChatChannelType.IncidentLane, CommandStructureNodeId = "node-1", Name = "node-1" });

			var command = BuildCommand();
			command.Name = "Barn Fire";

			await BuildService().EnsureIncidentChannelsAsync(command, new[] { BuildNode("node-1") });

			_channelRepository.Verify(x => x.UpdateChannelInfoAsync("a", "Barn Fire", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_channelRepository.Verify(x => x.UpdateChannelInfoAsync("b", "Barn Fire Command (private)", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			_channelRepository.Verify(x => x.UpdateChannelInfoAsync("c", "Barn Fire node-1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

			// A rename changes what connected clients display — cached verdicts and lists must roll.
			_permissionService.Verify(x => x.InvalidateChannelCacheAsync("a"), Times.Once);
		}
	}
}
