using System;
using System.Collections.Generic;
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
	public class DiagnosticProtectionTests
	{
		[Test]
		public async Task Request_encryption_binds_department_owner_and_run_and_fails_closed_on_redaction()
		{
			var key = SecurityConfig.EncryptionKey; var salt = SecurityConfig.EncryptionSaltValue;
			SecurityConfig.EncryptionKey = Guid.NewGuid().ToString("N"); SecurityConfig.EncryptionSaltValue = Guid.NewGuid().ToString("N");
			try
			{
				var actor = new AdminAssistActor(7, "admin"); var writer = new Mock<IProtectedWriteService>();
				writer.Setup(w => w.PreflightWriteAsync(7, null, "admin", false, It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedWriteResult.Allowed());
				writer.Setup(w => w.PrepareRecordsEntityWriteAsync(7, It.IsAny<AdminAssistDiagnosticRun>(), null, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, (Func<AdminAssistDiagnosticRun, string>, Action<AdminAssistDiagnosticRun, string>)>>(), It.IsAny<Action>(), null, "admin", false, It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedWriteResult.Allowed());
				var protection = new Mock<IDepartmentDataProtectionService>();
				var service = new AdminAssistDiagnosticProtection(new EncryptionService(), writer.Object, Mock.Of<IProtectedReadService>(), protection.Object, Mock.Of<IProtectedGrantContext>());
				var row = new AdminAssistDiagnosticRun { Id = Guid.NewGuid().ToString("D"), DepartmentId = 7, UserId = "admin" };
				var request = new DiagnosticRequest("paging", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, 123, "private-member");
				await service.ProtectAsync(actor, row, request, CancellationToken.None); var ciphertext = row.Content;
				Assert.That(ciphertext, Does.StartWith("enc2:").And.Not.Contain("private-member"));
				Assert.That(await service.ReadAsync(actor, row, CancellationToken.None), Is.EqualTo(request));
				row.Id = Guid.NewGuid().ToString("D"); row.Content = ciphertext;
				Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync(actor, row, CancellationToken.None));
				row.Content = ProtectedDataEnvelope.RedactionValue;
				Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync(actor, row, CancellationToken.None));
				Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync(actor with { UserId = "other" }, row, CancellationToken.None));
				protection.Setup(p => p.ShouldEncryptNewWritesAsync(7)).ReturnsAsync(true);
				protection.Setup(p => p.GetPinnedCatalogVersionAsync(7)).ReturnsAsync(31);
				Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequireAsync(actor, CancellationToken.None));
			}
			finally { SecurityConfig.EncryptionKey = key; SecurityConfig.EncryptionSaltValue = salt; }
		}
	}
}
