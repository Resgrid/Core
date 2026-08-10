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
		private List<ChatChannel> _inserted;

		[SetUp]
		public void Setup()
		{
			_channelRepository = new Mock<IChatChannelRepository>();
			_cacheProvider = new Mock<ICacheProvider>();
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
				Mock.Of<IChatPermissionService>(),
				Mock.Of<IDepartmentsService>(),
				Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IUnitsService>(),
				Mock.Of<IUserProfileService>(),
				Mock.Of<IEventAggregator>(),
				_cacheProvider.Object,
				Mock.Of<IUnitOfWork>());

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
	}
}
