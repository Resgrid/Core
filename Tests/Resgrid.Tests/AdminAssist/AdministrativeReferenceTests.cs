using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class AdministrativeReferenceTests
	{
		[Test]
		public async Task Duplicate_policy_links_are_checked_once_and_only_metadata_counts_leave_the_adapter()
		{
			var settings = new Mock<IDepartmentSettingsService>(); var store = new Mock<IAdministrativeReferenceStore>(); var now = DateTime.UtcNow;
			settings.Setup(s => s.GetOperatingProfileAsync(7)).ReturnsAsync(new DepartmentOperatingProfile {
				StaffingPolicyReferences = new() { "1234567" }, QualificationPolicyReferences = new() { "1234567" }, ContinuityProcedureReferences = new() { "9876543" }, SiteGroupReferences = new() { "7" }, AuthoritativeSystemReferences = new() { "private-system" } });
			store.Setup(s => s.ReadAdministrativeReferencesAsync(7, It.Is<int[]>(ids => ids.Length == 2 && ids.Contains(1234567) && ids.Contains(9876543)), It.Is<int[]>(ids => ids.SequenceEqual(new[] { 7 })), now, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new AdministrativeReferenceCounts(2, 1, 1, 1, 0));
			var result = await new AdministrativeReferenceEvidenceSource(settings.Object, store.Object).ReadAsync(new(7, "admin"), now, CancellationToken.None);
			Assert.That(result.Single(e => e.Id == "unavailablePolicyReferences").Number, Is.EqualTo(1));
			Assert.That(result.Single(e => e.Id == "declaredContinuityReferences").Number, Is.EqualTo(1));
			Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(result), Does.Not.Contain("1234567").And.Not.Contain("9876543").And.Not.Contain("private-system"));
		}
		[Test]
		public void Missing_or_changing_reference_source_is_not_known_zero()
		{
			var settings = new Mock<IDepartmentSettingsService>(); var store = new Mock<IAdministrativeReferenceStore>();
			settings.Setup(s => s.GetOperatingProfileAsync(7)).ReturnsAsync(new DepartmentOperatingProfile());
			var source = new AdministrativeReferenceEvidenceSource(settings.Object, store.Object);
			Assert.ThrowsAsync<InvalidOperationException>(async () => await source.ReadAsync(new(7, "admin"), DateTime.UtcNow, CancellationToken.None));
			store.SetupSequence(s => s.ReadAdministrativeReferencesAsync(7, It.IsAny<int[]>(), It.IsAny<int[]>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new AdministrativeReferenceCounts(1, 0, 0, 0, 0)).ReturnsAsync(new AdministrativeReferenceCounts(1, 1, 0, 0, 0));
			Assert.ThrowsAsync<InvalidOperationException>(async () => await source.ReadAsync(new(7, "admin"), DateTime.UtcNow, CancellationToken.None));
		}
	}
}
