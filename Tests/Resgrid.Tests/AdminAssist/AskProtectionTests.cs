using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture, NonParallelizable]
	public class AskProtectionTests
	{
		[Test]
		public async Task Non_adp_conversations_are_authenticated_encrypted_and_bound_to_the_owner_and_row()
		{
			// Arrange
			var previousKey = SecurityConfig.EncryptionKey; var previousSalt = SecurityConfig.EncryptionSaltValue;
			SecurityConfig.EncryptionKey = Guid.NewGuid().ToString("N"); SecurityConfig.EncryptionSaltValue = Guid.NewGuid().ToString("N");
			try {
				var actor = new AdminAssistActor(7, "admin"); var writer = new Mock<IProtectedWriteService>(); var reader = new Mock<IProtectedReadService>();
				writer.Setup(w => w.PreflightWriteAsync(7, null, "admin", false, It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedWriteResult.Allowed());
				writer.Setup(w => w.PrepareRecordsEntityWriteAsync(7, It.IsAny<AiGenerationRow>(), null, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, (Func<AiGenerationRow, string>, Action<AiGenerationRow, string>)>>(), It.IsAny<Action>(), null, "admin", false, It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedWriteResult.Allowed());
				var service = new AdminAssistConversationProtection(new EncryptionService(), new(() => writer.Object), new(() => reader.Object), Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IProtectedGrantContext>());
				var row = new AiGenerationRow { Id = Guid.NewGuid().ToString("D"), ConversationId = Guid.NewGuid().ToString("D"), DepartmentId = 7, UserId = "admin", Content = JsonSerializer.Serialize(new AskStoredContent("private question", Array.Empty<AskToolInput>(), Array.Empty<string>())) };
				// Act
				await service.ProtectAsync(actor, row, CancellationToken.None); var encrypted = row.Content;
				var restored = await service.ReadAsync(actor, row, CancellationToken.None);
				// Assert
				Assert.That(encrypted, Does.StartWith("enc2:").And.Not.Contain("private question")); Assert.That(restored.Question, Is.EqualTo("private question"));
				row.Id = Guid.NewGuid().ToString("D"); row.Content = encrypted;
				Assert.That(await service.ReadAsync(actor, row, CancellationToken.None), Is.Null);
				Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.ReadAsync(actor with { UserId = "other" }, row, CancellationToken.None));
			} finally { SecurityConfig.EncryptionKey = previousKey; SecurityConfig.EncryptionSaltValue = previousSalt; }
		}
		[Test]
		public async Task Redacted_history_never_reaches_the_application_decryptor()
		{
			// Arrange
			var encryption = new Mock<IEncryptionService>(MockBehavior.Strict);
			var row = new AiGenerationRow { Id = "row", DepartmentId = 7, UserId = "admin", Content = ProtectedDataEnvelope.RedactionValue, IsProtected = true, ProtectedCatalogVersion = 31 };
			var service = new AdminAssistConversationProtection(encryption.Object, new(() => Mock.Of<IProtectedWriteService>()), new(() => Mock.Of<IProtectedReadService>()), Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IProtectedGrantContext>());
			// Act
			var result = await service.ReadAsync(new(7, "admin"), row, CancellationToken.None);
			// Assert
			Assert.That(result, Is.Null); encryption.VerifyNoOtherCalls();
		}
	}
}
