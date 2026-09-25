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
	public class OperatingProfileEvidenceTests
	{
		[Test]
		public async Task Combined_packs_are_canonical_and_no_system_or_document_references_leave_adapter()
		{
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetOperatingProfileAsync(7)).ReturnsAsync(new DepartmentOperatingProfile {
				Archetypes = new() { "mental-health", "ems" }, AuthoritativeSystemReferences = new() { "private-system" }, StaffingPolicyReferences = new() { "123" }, Revision = 3, ReviewedOnUtc = DateTime.UtcNow
			});
			var source = new OperatingProfileEvidenceSource(settings.Object);
			var result = await source.ReadAsync(new(7, "admin"), DateTime.UtcNow, CancellationToken.None);
			Assert.That(result.Single(e => e.Id == "operatingPackIds").Code, Is.EqualTo("ems,mental-health"));
			Assert.That(string.Join(" ", result.Select(e => e.ToString())), Does.Not.Contain("private-system").And.Not.Contain("123"));
		}
		[Test]
		public void Selected_area_without_rules_is_never_reported_as_covered()
		{
			var report = new ConfigurationReport(new(7, "admin", "1", DateTime.UtcNow, true, new System.Collections.Generic.Dictionary<string, ConfigurationEvidence>()), Array.Empty<ConfigurationFinding>(), new[] { "business", "knowledge" });
			Assert.That(report.UncheckedAreaIds, Is.EquivalentTo(new[] { "business", "knowledge" }));
			Assert.That(report.Verified, Is.Zero);
		}
	}
}
