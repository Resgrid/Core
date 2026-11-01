using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Communications;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard;
using Resgrid.Services.Dashboard.Widgets.Communications;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class VoicePttWidgetProviderTests
	{
		private static Mock<IVoiceService> Voice()
		{
			var voice = new Mock<IVoiceService>(MockBehavior.Strict);
			voice.Setup(x => x.GetVoiceSettingsForDepartmentAsync(DepartmentId)).ReturnsAsync(new DepartmentVoice
			{
				DepartmentId = DepartmentId,
				Channels = new List<DepartmentVoiceChannel>
				{
					new DepartmentVoiceChannel { DepartmentVoiceChannelId = "tac", DepartmentId = DepartmentId, Name = "Tac 1", IsOnDemand = true, CallId = 5 },
					new DepartmentVoiceChannel { DepartmentVoiceChannelId = "closed", DepartmentId = DepartmentId, Name = "Old incident", IsOnDemand = true, ClosedOn = Now },
					new DepartmentVoiceChannel { DepartmentVoiceChannelId = "main", DepartmentId = DepartmentId, Name = "Main", IsDefault = true, ConferenceNumber = 1001 },
					new DepartmentVoiceChannel { DepartmentVoiceChannelId = "foreign", DepartmentId = OtherDepartmentId, Name = "Not ours" }
				}
			});
			return voice;
		}

		[Test]
		public void Is_gated_by_the_voice_policy_and_the_push_to_talk_addon()
		{
			var gates = DashboardWidgetCatalog.Find("voice.ptt").Gates;

			gates.Policies.Should().Contain("Voice_View");
			gates.Access.Should().Contain(DashboardAccessChecks.VoiceAddon);
			gates.Addon.Should().Be(PlanAddonTypes.PTT);
		}

		[Test]
		public async Task Lists_open_channels_default_first_with_seat_utilization()
		{
			var voice = Voice();
			voice.Setup(x => x.GetCurrentUtilizationForLiveKit(DepartmentId)).ReturnsAsync(new DepartmentVoiceUtilization { SeatLimit = 20, CurrentlyActive = 4 });
			var provider = new VoicePttWidgetProvider(voice.Object);

			var data = (VoicePttWidgetData)await provider.GetDataAsync(For("voice.ptt"), CancellationToken.None);

			data.Channels.Select(c => c.DepartmentVoiceChannelId).Should().Equal("main", "tac");
			data.UtilizationAvailable.Should().BeTrue();
			data.SeatLimit.Should().Be(20);
			data.CurrentlyActive.Should().Be(4);
		}

		[Test]
		public async Task A_utilization_failure_still_shows_the_channels()
		{
			var voice = Voice();
			voice.Setup(x => x.GetCurrentUtilizationForLiveKit(DepartmentId)).ThrowsAsync(new InvalidOperationException("LiveKit down"));
			var provider = new VoicePttWidgetProvider(voice.Object);

			var data = (VoicePttWidgetData)await provider.GetDataAsync(For("voice.ptt"), CancellationToken.None);

			data.UtilizationAvailable.Should().BeFalse();
			data.Channels.Should().HaveCount(2);
		}
	}
}
