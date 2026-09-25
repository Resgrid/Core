using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Config;
using Resgrid.Llm;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture, NonParallelizable]
	public class AskServiceTests
	{
		private static readonly AdminAssistActor Actor = new(7, "admin", "en");
		[Test]
		public async Task Host_kill_switch_denies_without_billing_database_or_inference()
		{
			// Arrange
			var original = AiConfig.AdminAssistEnabled; AiConfig.AdminAssistEnabled = false;
			var access = new Mock<IAdminAssistAccessService>(MockBehavior.Strict); var flags = new Mock<IFeatureToggleService>(MockBehavior.Strict);
			try {
				var service = new AiAccessService(access.Object, flags.Object, Mock.Of<IDepartmentSettingsService>(), Mock.Of<IEnhancedAiAccessService>(), Mock.Of<IAiUsageMeter>(), Mock.Of<IAiFreeAllowanceStore>(), Mock.Of<IDepartmentDataProtectionService>(), TimeProvider.System);
				// Act
				var result = await service.CanUseAdminAssistAsync(Actor, CancellationToken.None);
				// Assert
				Assert.That(result.Available, Is.False); Assert.That(result.Reason, Is.EqualTo("Disabled")); access.VerifyNoOtherCalls(); flags.VerifyNoOtherCalls();
			} finally { AiConfig.AdminAssistEnabled = original; }
		}
		private sealed class Fixture
		{
			public readonly Mock<IAiAccessService> Access = new();
			public readonly Mock<IAiUsageMeter> Usage = new();
			public readonly Mock<IAdminAssistAskQueries> Queries = new();
			public readonly Mock<IAdminAssistConversationStore> Store = new();
			public readonly Mock<IAdminAssistConversationProtection> Protection = new();
			public readonly Mock<ILlmClient> Client = new();
			public readonly AdminAssistAskService Service;
			public Fixture() {
				Access.Setup(a => a.CanUseAdminAssistAsync(Actor, It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(new AdminAssistAskStatus(true, "Available", 100000));
				var ordinary = new Mock<IAdminAssistAccessService>(); ordinary.Setup(a => a.CanAccessAsync(Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Access.Setup(u => u.ReserveTurnAsync(Actor, It.IsAny<AdminAssistAskStatus>(), It.IsAny<CancellationToken>())).ReturnsAsync(new AiUsageReservation("reservation", 7, "admin", 32768, DateTime.UtcNow.AddMinutes(2)));
				Queries.Setup(q => q.ReadAsync(Actor, It.IsAny<AskToolInput>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] {
					new AskEvidence("setup:report", "Setup", "Ui.report", Array.Empty<string>(), new Dictionary<string, decimal>(), "Known", null, "setup:report", "1", "1", DateTime.UtcNow) });
				Service = new(Access.Object, ordinary.Object, Usage.Object, Queries.Object, new ConfigurationCatalog(), Store.Object, Protection.Object, new Lazy<ILlmClient>(() => Client.Object), TimeProvider.System);
			}
		}
		[Test]
		public void Disabled_access_never_reserves_or_sends()
		{
			// Arrange
			var f = new Fixture(); f.Access.Setup(a => a.CanUseAdminAssistAsync(Actor, It.IsAny<CancellationToken>(), true)).ReturnsAsync(new AdminAssistAskStatus(false, "Disabled", 0));
			// Act / Assert
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.AskAsync(Actor, new("Setup help"), CancellationToken.None));
			f.Usage.VerifyNoOtherCalls(); f.Client.VerifyNoOtherCalls(); f.Store.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Ambiguous_provider_failure_is_charged_without_saving_or_echoing_question()
		{
			// Arrange
			var f = new Fixture(); f.Client.Setup(c => c.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("provider secret"));
			// Act
			var answer = await f.Service.AskAsync(Actor, new("Patient secret in question"), CancellationToken.None);
			// Assert
			Assert.That(answer.Outcome, Is.EqualTo("Unavailable")); Assert.That(answer.Evidence, Is.Empty); Assert.That(answer.ConversationId, Is.Null);
			f.Store.Verify(s => s.SaveAsync(It.IsAny<AdminAssistActor>(), It.IsAny<AiGenerationRow>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
			f.Usage.Verify(u => u.CompleteAsync(It.IsAny<AiUsageReservation>(), 32768, "Unavailable", It.IsAny<CancellationToken>()), Times.Once);
		}
		[Test]
		public async Task Successful_turn_saves_only_after_protection_and_consumes_actual_usage()
		{
			// Arrange
			var oldHmac = AiConfig.AuditHmacKey; AiConfig.AuditHmacKey = Convert.ToBase64String(new byte[32]);
			try {
				var f = new Fixture(); f.Client.Setup(c => c.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new LlmResult("{\"evidenceIds\":[\"setup:report\"],\"abstain\":false}", Array.Empty<LlmToolCall>(), 1000, 20, "stop"));
				f.Protection.Setup(p => p.ProtectAsync(Actor, It.IsAny<AiGenerationRow>(), It.IsAny<CancellationToken>())).Callback<AdminAssistActor, AiGenerationRow, CancellationToken>((_, row, _) => row.Content = "enc2:cipher").Returns(Task.CompletedTask);
				AiGenerationRow saved = null;
				f.Store.Setup(s => s.SaveAsync(Actor, It.IsAny<AiGenerationRow>(), 0, It.IsAny<CancellationToken>())).Callback<AdminAssistActor, AiGenerationRow, long, CancellationToken>((_, row, _, _) => { saved = row; row.Revision = 1; }).Returns(Task.CompletedTask);
				// Act
				var answer = await f.Service.AskAsync(Actor, new("Setup"), CancellationToken.None);
				// Assert
				Assert.That(answer.Outcome, Is.EqualTo("Answered")); Assert.That(answer.Revision, Is.EqualTo(1)); Assert.That(saved.Content, Is.EqualTo("enc2:cipher")); Assert.That(saved.RequestDigest.Length, Is.EqualTo(64));
				f.Usage.Verify(u => u.CompleteAsync(It.IsAny<AiUsageReservation>(), 1020, "Answered", It.IsAny<CancellationToken>()), Times.Once);
			} finally { AiConfig.AuditHmacKey = oldHmac; }
		}
		[Test]
		public void Owner_check_precedes_model_admission_on_existing_conversation()
		{
			// Arrange
			var f = new Fixture(); var id = Guid.NewGuid().ToString("D"); f.Store.Setup(s => s.GetRevisionAsync(Actor, id, It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());
			// Act / Assert
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.AskAsync(Actor, new("Setup", id, 1), CancellationToken.None));
			f.Usage.VerifyNoOtherCalls(); f.Client.VerifyNoOtherCalls();
		}
	}
}
