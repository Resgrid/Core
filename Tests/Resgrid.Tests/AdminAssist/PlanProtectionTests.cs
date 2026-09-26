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
	public class PlanProtectionTests
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
				writer.Setup(w => w.PrepareRecordsEntityWriteAsync(7, It.IsAny<AdminAssistPlanRow>(), null, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, (Func<AdminAssistPlanRow, string>, Action<AdminAssistPlanRow, string>)>>(), It.IsAny<Action>(), null, "admin", false, It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedWriteResult.Allowed());
				var protection = new Mock<IDepartmentDataProtectionService>();
				var service = new AdminAssistPlanProtection(new EncryptionService(), writer.Object, Mock.Of<IProtectedReadService>(), protection.Object, Mock.Of<IProtectedGrantContext>());
				var row = new AdminAssistPlanRow { Id = Guid.NewGuid().ToString("D"), DepartmentId = 7, UserId = "admin" };
				var request = new PlanContent(new PlanDraftRequest("private-member goal", "admin-security"), Array.Empty<string>(), new Dictionary<string, string>(), new Dictionary<string, string>(), Array.Empty<PlanAttestation>());
				await service.ProtectAsync(actor, row, request, CancellationToken.None); var ciphertext = row.Content;
				Assert.That(ciphertext, Does.StartWith("enc2:").And.Not.Contain("private-member"));
				Assert.That((await service.ReadAsync(actor, row, CancellationToken.None)).Draft.Goal, Is.EqualTo(request.Draft.Goal));
				row.Id = Guid.NewGuid().ToString("D"); row.Content = ciphertext;
				Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync(actor, row, CancellationToken.None));
				row.Content = ProtectedDataEnvelope.RedactionValue;
				Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync(actor, row, CancellationToken.None));
				Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ReadAsync(actor with { UserId = "other" }, row, CancellationToken.None));
				protection.Setup(p => p.ShouldEncryptNewWritesAsync(7)).ReturnsAsync(true);
				protection.Setup(p => p.GetPinnedCatalogVersionAsync(7)).ReturnsAsync(32);
				Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequireAsync(actor, CancellationToken.None));
			}
			finally { SecurityConfig.EncryptionKey = key; SecurityConfig.EncryptionSaltValue = salt; }
		}
		[Test]
		public async Task Rewrite_takes_its_protection_markers_from_this_write_not_the_stored_row()
		{
			var key = SecurityConfig.EncryptionKey; var salt = SecurityConfig.EncryptionSaltValue;
			SecurityConfig.EncryptionKey = Guid.NewGuid().ToString("N"); SecurityConfig.EncryptionSaltValue = Guid.NewGuid().ToString("N");
			try
			{
				var actor = new AdminAssistActor(7, "admin"); var writer = new Mock<IProtectedWriteService>(); Action markProtected = null;
				writer.Setup(w => w.PreflightWriteAsync(7, null, "admin", false, It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedWriteResult.Allowed());
				// The department no longer encrypts new writes, so the protected write never calls markProtected.
				writer.Setup(w => w.PrepareRecordsEntityWriteAsync(7, It.IsAny<AdminAssistPlanRow>(), null, It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, (Func<AdminAssistPlanRow, string>, Action<AdminAssistPlanRow, string>)>>(), It.IsAny<Action>(), null, "admin", false, It.IsAny<CancellationToken>()))
					.Callback(new InvocationAction(i => markProtected = (Action)i.Arguments[5])).ReturnsAsync(ProtectedWriteResult.Allowed());
				var service = new AdminAssistPlanProtection(new EncryptionService(), writer.Object, Mock.Of<IProtectedReadService>(), Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IProtectedGrantContext>());
				var row = new AdminAssistPlanRow { Id = Guid.NewGuid().ToString("D"), DepartmentId = 7, UserId = "admin", IsProtected = true, ProtectedCatalogVersion = ProtectedFieldCatalog.AdminAssistPlansCatalogVersion };
				var request = new PlanContent(new PlanDraftRequest("goal", "admin-security"), Array.Empty<string>(), new Dictionary<string, string>(), new Dictionary<string, string>(), Array.Empty<PlanAttestation>());
				await service.ProtectAsync(actor, row, request, CancellationToken.None);
				Assert.That(row.Content, Does.StartWith("enc2:")); Assert.That(row.IsProtected, Is.False); Assert.That(row.ProtectedCatalogVersion, Is.Null);
				markProtected();
				Assert.That(row.IsProtected, Is.True); Assert.That(row.ProtectedCatalogVersion, Is.EqualTo(ProtectedFieldCatalog.AdminAssistPlansCatalogVersion));
			}
			finally { SecurityConfig.EncryptionKey = key; SecurityConfig.EncryptionSaltValue = salt; }
		}
	}
}
