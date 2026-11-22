using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class VoiceEvidenceTests
	{
		private const int DepartmentId = 7;
		private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

		private static async Task<Dictionary<string, ConfigurationEvidence>> Read(Mock<IVoiceService> voice) =>
			(await new VoiceEvidenceSource(voice.Object).ReadAsync(new AdminAssistActor(DepartmentId, "admin"), Now, CancellationToken.None)).ToDictionary(e => e.Id);

		private static DepartmentVoiceChannel Channel(string name, bool onDemand = false) =>
			new() { DepartmentVoiceChannelId = Guid.NewGuid().ToString(), DepartmentVoiceId = "voice", DepartmentId = DepartmentId, Name = name, IsOnDemand = onDemand };

		[Test]
		public async Task Standing_channels_are_counted_and_no_channel_content_leaves_the_source()
		{
			var voice = new Mock<IVoiceService>();
			voice.Setup(v => v.CanDepartmentUseVoiceAsync(DepartmentId)).ReturnsAsync(true);
			voice.Setup(v => v.GetVoiceSettingsForDepartmentAsync(DepartmentId)).ReturnsAsync(new DepartmentVoice
			{
				DepartmentVoiceId = "voice", DepartmentId = DepartmentId,
				Channels = new List<DepartmentVoiceChannel> { Channel("Fireground"), Channel("Command"), Channel("Incident 42", onDemand: true) }
			});
			var source = new VoiceEvidenceSource(voice.Object);

			var evidence = await Read(voice);

			Assert.That(evidence.Keys, Is.EquivalentTo(source.EvidenceIds));
			Assert.That((evidence["pttAvailable"].State, evidence["pttAvailable"].Boolean), Is.EqualTo((EvidenceState.Known, (bool?)true)));
			Assert.That(evidence["voiceChannelCount"].Number, Is.EqualTo(2m), "An on-demand incident channel closes with its incident.");
			Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(evidence.Values), Does.Not.Contain("Fireground"));
		}

		[Test]
		public async Task A_department_without_a_voice_record_has_no_channels()
		{
			var voice = new Mock<IVoiceService>();
			voice.Setup(v => v.CanDepartmentUseVoiceAsync(DepartmentId)).ReturnsAsync(false);
			voice.Setup(v => v.GetVoiceSettingsForDepartmentAsync(DepartmentId)).ReturnsAsync((DepartmentVoice)null);

			var evidence = await Read(voice);

			Assert.That((evidence["pttAvailable"].State, evidence["pttAvailable"].Boolean), Is.EqualTo((EvidenceState.Known, (bool?)false)));
			Assert.That((evidence["voiceChannelCount"].State, evidence["voiceChannelCount"].Number), Is.EqualTo((EvidenceState.Known, (decimal?)0)));
		}

		[Test]
		public async Task Each_fact_is_read_independently_and_a_failure_is_unknown_not_false_or_zero()
		{
			var voice = new Mock<IVoiceService>();
			voice.Setup(v => v.CanDepartmentUseVoiceAsync(DepartmentId)).ThrowsAsync(new InvalidOperationException("billing timeout"));
			voice.Setup(v => v.GetVoiceSettingsForDepartmentAsync(DepartmentId)).ReturnsAsync(new DepartmentVoice
				{ DepartmentVoiceId = "voice", DepartmentId = DepartmentId, Channels = new List<DepartmentVoiceChannel> { Channel("Ops") } });

			var evidence = await Read(voice);

			Assert.That((evidence["pttAvailable"].State, evidence["pttAvailable"].Boolean), Is.EqualTo((EvidenceState.Unknown, (bool?)null)));
			Assert.That(evidence["pttAvailable"].ReasonCode, Is.EqualTo("SourceUnavailable"));
			Assert.That(evidence["voiceChannelCount"].Number, Is.EqualTo(1m));

			voice.Setup(v => v.GetVoiceSettingsForDepartmentAsync(DepartmentId)).ReturnsAsync(new DepartmentVoice { DepartmentVoiceId = "other", DepartmentId = 8 });
			Assert.That((await Read(voice))["voiceChannelCount"].State, Is.EqualTo(EvidenceState.Unknown), "Another department's record is never counted.");
		}
	}
}
