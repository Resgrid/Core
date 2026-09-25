using System;
using System.Collections.Generic;
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
	public class StatusAutomationImpactTests
	{
		[TestCase(false)] [TestCase(true)]
		public async Task Reset_preview_uses_actual_status_projection_and_restricted_person_is_unknown(bool restricted)
		{
			var now = DateTime.UtcNow; var actions = new Mock<IActionLogsRepository>(); var visibility = new Mock<IAuthorizationService>(); var membership = new Mock<IRecordsAuthorizationService>();
			actions.Setup(a => a.ReadLatestForAdministrationAsync(7, true, now, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActionLog> { new() { DepartmentId = 7, UserId = "person", ActionTypeId = (int)ActionTypes.RespondingToScene } });
			actions.Setup(a => a.ReadLatestForAdministrationAsync(7, false, now, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ActionLog>());
			visibility.Setup(v => v.CanUserViewPersonAsync("admin", "person", 7)).ReturnsAsync(!restricted);
			membership.Setup(m => m.IsAssignableMemberAsync("person", 7)).ReturnsAsync(true);
			var source = new StatusAutomationImpactProvider(actions.Object, visibility.Object, membership.Object);
			var facts = new Dictionary<string, ConfigurationEvidence> { ["DisabledAutoAvailable"] = new("DisabledAutoAvailable", EvidenceState.Known, "test", "1", now, Boolean: true) };
			var result = await source.EvaluateAsync(new(7, "admin"), new(7, "admin", "1", now, true, facts), new("setting.DisabledAutoAvailable", "1", false), CancellationToken.None);
			Assert.That(result.Metrics[0].State, Is.EqualTo(restricted ? EvidenceState.Redacted : EvidenceState.Known));
			Assert.That(result.Metrics[0].After, Is.EqualTo(restricted ? (decimal?)null : 1));
			if (!restricted) { Assert.That(result.Metrics[1].Before, Is.Zero); Assert.That(result.Metrics[1].After, Is.EqualTo(1)); }
		}
	}
}
