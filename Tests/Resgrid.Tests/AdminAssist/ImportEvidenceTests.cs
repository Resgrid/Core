using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class ImportEvidenceTests
	{
		[TestCase(null, 60, EvidenceState.NotApplicable, null)]
		[TestCase(30, 60, EvidenceState.Known, true)]
		[TestCase(30, 5, EvidenceState.Known, false)]
		[TestCase(30, -5, EvidenceState.Unknown, null)]
		[TestCase(30, 0, EvidenceState.Unknown, null)]
		public async Task Polling_uses_declared_interval_and_recorded_poll_not_call_volume(int? expected, int age, EvidenceState state, bool? missing)
		{
			var now = DateTime.UtcNow;
			var rows = new Mock<IDepartmentCallEmailsRepository>(); var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetOperatingProfileAsync(7)).ReturnsAsync(new DepartmentOperatingProfile { ExpectedEmailPollIntervalMinutes = expected });
			rows.Setup(r => r.GetAllByDepartmentIdAsync(7)).ReturnsAsync(new[] { new DepartmentCallEmail { DepartmentId = 7,
				LastCheck = age == 0 ? null : now.AddMinutes(-age), IsFailure = true, Username = "private-mailbox", Password = "secret", ErrorMessage = "private-error" } });
			var source = new ImportEvidenceSource(rows.Object, settings.Object);
			var facts = await source.ReadAsync(new(7, "admin"), now, CancellationToken.None);
			var heartbeat = facts.Single(f => f.Id == "importHeartbeatMissing");
			Assert.That(heartbeat.State, Is.EqualTo(state)); Assert.That(heartbeat.Boolean, Is.EqualTo(missing));
			Assert.That(facts.Single(f => f.Id == "emailImportFailureCount").Number, Is.EqualTo(1));
			var json = Newtonsoft.Json.JsonConvert.SerializeObject(facts);
			Assert.That(json, Does.Not.Contain("private-").And.Not.Contain("secret"));
		}
		[Test]
		public async Task No_connector_with_declared_expectation_is_unknown_and_cross_tenant_evidence_is_rejected()
		{
			var rows = new Mock<IDepartmentCallEmailsRepository>(); var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetOperatingProfileAsync(7)).ReturnsAsync(new DepartmentOperatingProfile { ExpectedEmailPollIntervalMinutes = 30 });
			rows.Setup(r => r.GetAllByDepartmentIdAsync(7)).ReturnsAsync(Array.Empty<DepartmentCallEmail>());
			var source = new ImportEvidenceSource(rows.Object, settings.Object);
			Assert.That((await source.ReadAsync(new(7, "admin"), DateTime.UtcNow, CancellationToken.None)).Single(f => f.Id == "importHeartbeatMissing").State, Is.EqualTo(EvidenceState.Unknown));
			rows.Setup(r => r.GetAllByDepartmentIdAsync(7)).ReturnsAsync(new[] { new DepartmentCallEmail { DepartmentId = 8 } });
			Assert.ThrowsAsync<InvalidOperationException>(async () => await source.ReadAsync(new(7, "admin"), DateTime.UtcNow, CancellationToken.None));
		}
	}
}
