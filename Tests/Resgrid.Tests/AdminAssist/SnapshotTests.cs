using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class SnapshotTests
	{
		private static readonly AdminAssistActor Actor = new(7, "admin");
		private Mock<IRecordsAuthorizationService> Authorize()
		{
			var auth = new Mock<IRecordsAuthorizationService>();
			auth.Setup(a => a.IsActiveMemberAsync(Actor.UserId, Actor.DepartmentId)).ReturnsAsync(true);
			auth.Setup(a => a.IsDepartmentAdminAsync(Actor.UserId, Actor.DepartmentId)).ReturnsAsync(true);
			return auth;
		}
		private Mock<IAdminAssistEvidenceSource> Source(Func<Task<IReadOnlyList<ConfigurationEvidence>>> read)
		{
			var source = new Mock<IAdminAssistEvidenceSource>(); source.SetupGet(s => s.SourceId).Returns("test");
			source.SetupGet(s => s.EvidenceIds).Returns(new[] { "unitCount" });
			source.Setup(s => s.ReadAsync(Actor, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).Returns(read);
			return source;
		}
		[Test]
		public void Demoted_administrator_is_rejected_before_reading_any_evidence()
		{
			var auth = Authorize(); auth.Setup(a => a.IsDepartmentAdminAsync(Actor.UserId, Actor.DepartmentId)).ReturnsAsync(false);
			var source = Source(() => Task.FromResult<IReadOnlyList<ConfigurationEvidence>>(Array.Empty<ConfigurationEvidence>()));
			var provider = new ConfigurationSnapshotProvider(new[] { source.Object }, Mock.Of<IAdminAssistRepository>(), auth.Object, new ConfigurationCatalog(), TimeProvider.System);
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ReadAsync(Actor));
			source.Verify(s => s.ReadAsync(It.IsAny<AdminAssistActor>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
		}
		[Test]
		public void Mid_read_revocation_discards_the_entire_response()
		{
			var auth = Authorize();
			auth.SetupSequence(a => a.IsDepartmentAdminAsync(Actor.UserId, Actor.DepartmentId)).ReturnsAsync(true).ReturnsAsync(false);
			var provider = new ConfigurationSnapshotProvider(Array.Empty<IAdminAssistEvidenceSource>(), Mock.Of<IAdminAssistRepository>(), auth.Object, new ConfigurationCatalog(), TimeProvider.System);
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.ReadAsync(Actor));
		}
		[TestCase(false)] [TestCase(true)]
		public async Task Missing_and_redacted_sources_never_become_zero_counts(bool redacted)
		{
			var source = Source(() => throw (redacted ? new UnauthorizedAccessException("private details") : new InvalidOperationException("private details")));
			var provider = new ConfigurationSnapshotProvider(new[] { source.Object }, Mock.Of<IAdminAssistRepository>(), Authorize().Object, new ConfigurationCatalog(), TimeProvider.System);
			var snapshot = await provider.ReadAsync(Actor);
			Assert.That(snapshot.Find("unitCount").State, Is.EqualTo(redacted ? EvidenceState.Redacted : EvidenceState.Unknown));
			Assert.That(snapshot.Find("unitCount").Number, Is.Null);
			Assert.That(snapshot.Evidence.Values.All(e => e.ReasonCode != "private details"), Is.True);
		}
		[Test]
		public async Task Configuration_mutation_during_source_reads_is_marked_inconsistent()
		{
			var repository = new Mock<IAdminAssistRepository>();
			repository.SetupSequence(r => r.GetConfigurationRevisionAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(3).ReturnsAsync(4);
			var provider = new ConfigurationSnapshotProvider(Array.Empty<IAdminAssistEvidenceSource>(), repository.Object, Authorize().Object, new ConfigurationCatalog(), TimeProvider.System);
			var snapshot = await provider.ReadAsync(Actor);
			Assert.That(snapshot.Consistent, Is.False); Assert.That(snapshot.Revision, Is.EqualTo("4"));
		}
		[TestCase(false)] [TestCase(true)]
		public async Task A_slow_or_internally_timed_out_source_becomes_unknown_without_failing_the_snapshot(bool internalTimeout)
		{
			var saved = Resgrid.Config.AdminAssistConfig.EvidenceSourceTimeoutSeconds;
			Resgrid.Config.AdminAssistConfig.EvidenceSourceTimeoutSeconds = 1;
			try
			{
				// Never completes (a hung billing call), or cancels on its own clock (a provider's HTTP timeout).
				var slow = Source(() => internalTimeout ? throw new TaskCanceledException("provider timeout") : new TaskCompletionSource<IReadOnlyList<ConfigurationEvidence>>().Task);
				var fast = new Mock<IAdminAssistEvidenceSource>(); fast.SetupGet(s => s.SourceId).Returns("fast");
				fast.SetupGet(s => s.EvidenceIds).Returns(new[] { "groupCount" });
				fast.Setup(s => s.ReadAsync(Actor, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).Returns((AdminAssistActor _, DateTime now, CancellationToken _) =>
					Task.FromResult<IReadOnlyList<ConfigurationEvidence>>(new[] { new ConfigurationEvidence("groupCount", EvidenceState.Known, "fast", "1", now, Number: 2) }));
				var provider = new ConfigurationSnapshotProvider(new[] { slow.Object, fast.Object }, Mock.Of<IAdminAssistRepository>(), Authorize().Object, new ConfigurationCatalog(), TimeProvider.System);
				var snapshot = await provider.ReadAsync(Actor);
				Assert.That(snapshot.Find("unitCount").State, Is.EqualTo(EvidenceState.Unknown));
				Assert.That(snapshot.Find("unitCount").ReasonCode, Is.EqualTo("SourceTimeout"));
				Assert.That(snapshot.Find("groupCount").Number, Is.EqualTo(2m), "Later sources still run after a slow one.");
			}
			finally { Resgrid.Config.AdminAssistConfig.EvidenceSourceTimeoutSeconds = saved; }
		}
	}
}
