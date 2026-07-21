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
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class IncidentVoiceServiceTests
	{
		[Test]
		public async Task CloseIncidentChannelsForCallAsync_WritesChannelClosedLog_EvenWhenCommandAlreadyClosed()
		{
			var voiceService = new Mock<IVoiceService>();
			var departmentsService = new Mock<IDepartmentsService>();
			var logRepo = new Mock<ICommandLogEntryRepository>();
			var commandRepo = new Mock<IIncidentCommandRepository>();
			var eventAggregator = new Mock<IEventAggregator>();
			var coreEventService = new Mock<ICoreEventService>();

			// One open on-demand channel on call 7.
			voiceService.Setup(x => x.GetVoiceSettingsForDepartmentAsync(10)).ReturnsAsync(new DepartmentVoice
			{
				Channels = new List<DepartmentVoiceChannel>
				{
					new DepartmentVoiceChannel { DepartmentVoiceChannelId = "ch1", IsOnDemand = true, CallId = 7, ClosedOn = null }
				}
			});
			voiceService.Setup(x => x.SaveOrUpdateVoiceChannelAsync(It.IsAny<DepartmentVoiceChannel>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DepartmentVoiceChannel c, int d, CancellationToken ct) => c);

			// The parent command is already CLOSED — the close-command flow closes it before auto-closing channels.
			commandRepo.Setup(x => x.GetAllByDepartmentIdAsync(10)).ReturnsAsync(new List<IncidentCommand>
			{
				new IncidentCommand
				{
					IncidentCommandId = "ic1", DepartmentId = 10, CallId = 7,
					Status = (int)IncidentCommandStatus.Closed, EstablishedOn = DateTime.UtcNow.AddHours(-1)
				}
			});
			logRepo.Setup(x => x.InsertAsync(It.IsAny<CommandLogEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((CommandLogEntry e, CancellationToken ct, bool b) => e);

			var service = new IncidentVoiceService(voiceService.Object, departmentsService.Object, logRepo.Object,
				new Mock<IVoiceTransmissionLogRepository>().Object, commandRepo.Object, eventAggregator.Object, coreEventService.Object);

			var result = await service.CloseIncidentChannelsForCallAsync(10, 7, "user1");

			result.Should().BeTrue();
			// The channel-closed entry is logged (inserted) against the (now Closed) command, not silently dropped.
			logRepo.Verify(x => x.InsertAsync(
				It.Is<CommandLogEntry>(e => e.EntryType == (int)CommandLogEntryType.ChannelClosed && e.IncidentCommandId == "ic1"),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			coreEventService.Verify(x => x.IncidentCommandUpdatedAsync(10, 7), Times.Once);
		}
	}
}
