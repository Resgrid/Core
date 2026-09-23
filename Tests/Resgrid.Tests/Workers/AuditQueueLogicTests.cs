using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Workers
{
	[TestFixture]
	public class AuditQueueLogicTests
	{
		private static Task<AuditLog> Build(AuditLogTypes type, string before = null, string after = null)
		{
			var userProfileService = new Mock<IUserProfileService>();
			userProfileService.Setup(x => x.GetProfileByUserIdAsync("actor", It.IsAny<bool>()))
				.ReturnsAsync(new UserProfile { UserId = "actor", FirstName = "Matt", LastName = "Casey" });

			return AuditQueueLogic.BuildAuditLogAsync(
				new AuditEvent { DepartmentId = 1, UserId = "actor", Type = type, Before = before, After = after },
				userProfileService.Object, Mock.Of<IAuditService>());
		}

		private static string CallPriority(string name) =>
			name == null ? null : JsonConvert.SerializeObject(new DepartmentCallPriority { Name = name });

		[TestCase(AuditLogTypes.CallPriorityAdded, "Matt Casey added a Call Priority.")]
		[TestCase(AuditLogTypes.CallPriorityEdited, "Matt Casey edited a Call Priority.")]
		[TestCase(AuditLogTypes.CallPriorityRemoved, "Matt Casey removed a Call Priority.")]
		[TestCase(AuditLogTypes.UnitTypeAdded, "Matt Casey added a Unit Type.")]
		[TestCase(AuditLogTypes.CertificationTypeRemoved, "Matt Casey removed a Certification Type.")]
		[TestCase(AuditLogTypes.DocumentCategoryAdded, "Matt Casey added a Document Category.")]
		[TestCase(AuditLogTypes.CustomStatusUpdated, "Matt Casey updated a Custom Status.")]
		[TestCase(AuditLogTypes.CustomStatusDetailUpdated, "Matt Casey updated a Custom Status Detail.")]
		public async Task Message_WithoutPayload_NamesTheActionTaken(AuditLogTypes type, string expected)
		{
			// These no-payload messages used to name the wrong action (an add logged as "removed", and so on).
			(await Build(type)).Message.Should().Be(expected);
		}

		[TestCase(AuditLogTypes.CallPriorityAdded, null, "Code 3", "Matt Casey added Call Priority Code 3")]
		[TestCase(AuditLogTypes.CallPriorityEdited, "Code 3", "Emergent", "Matt Casey edited Call Priority Code 3")]
		[TestCase(AuditLogTypes.CallPriorityEdited, "Code 3", null, "Matt Casey edited Call Priority Code 3")]
		[TestCase(AuditLogTypes.CallPriorityRemoved, "Code 3", null, "Matt Casey removed Call Priority Code 3")]
		public async Task CallPriority_Message_NamesTheActionAndPriority(AuditLogTypes type, string before, string after, string expected)
		{
			// Edited and Removed both logged "added"; Edited also read After while checking Before, so an
			// event with only a Before threw instead of being saved.
			(await Build(type, CallPriority(before), CallPriority(after))).Message.Should().Be(expected);
		}

		[Test]
		public async Task SettingsChanged_WithoutBefore_BuildsTheRow()
		{
			// The old Department deserialize threw ArgumentNullException here and the row was lost (RESGRID-WEBJOBS-88).
			var auditLog = await Build(AuditLogTypes.DepartmentSettingsChanged, null, "{\"Logo\":\"removed\"}");

			auditLog.Message.Should().Be("Matt Casey updated the department settings");
			auditLog.Data.Should().Be("Logo: removed");
		}

		[Test]
		public void SettingsChanged_ListsOnlyChangedValues()
		{
			var data = AuditQueueLogic.GetSettingsChangedAuditData(
				"{\"Name\":\"Station 1\",\"Use24HourTime\":false,\"Address\":{\"City\":\"Chicago\"}}",
				"{\"Name\":\"Station 1\",\"Use24HourTime\":true,\"Address\":{\"City\":\"Evanston\"}}");

			data.Should().Contain("Use24HourTime: false -> true");
			data.Should().Contain("Address.City: Chicago -> Evanston");
			data.Should().NotContain("Name");
		}

		[Test]
		public void SettingsChanged_ReportsValuesAddedAndRemovedBetweenSnapshots()
		{
			var data = AuditQueueLogic.GetSettingsChangedAuditData("{\"Website\":\"a.org\"}", "{\"Phone\":\"555\"}");

			data.Should().Contain("Website: a.org -> (none)");
			data.Should().Contain("Phone: (none) -> 555");
		}

		[Test]
		public void SettingsChanged_WithoutBefore_ListsAfterValues()
		{
			// Records cutover activation and the Profile logo/media key actions send only an After; the
			// old Department deserialize threw ArgumentNullException on the null Before (RESGRID-WEBJOBS-88).
			AuditQueueLogic.GetSettingsChangedAuditData(null, "{\"Logo\":\"uploaded crest.png\"}")
				.Should().Be("Logo: uploaded crest.png");
		}

		[Test]
		public void SettingsChanged_KeepsDateStringsAsSent()
		{
			AuditQueueLogic.GetSettingsChangedAuditData(null, "{\"ActivatedOn\":\"2026-09-23T03:14:55Z\"}")
				.Should().Be("ActivatedOn: 2026-09-23T03:14:55Z");
		}

		[Test]
		public void SettingsChanged_NonJsonPayload_IsRecordedVerbatim()
		{
			// The shape the Profile page sent for logo and media key changes (RESGRID-WEBJOBS-9D).
			AuditQueueLogic.GetSettingsChangedAuditData("logo", "uploaded crest.png")
				.Should().Be("Before: logo; After: uploaded crest.png");
			AuditQueueLogic.GetSettingsChangedAuditData("mediaKey", "regenerated")
				.Should().Be("Before: mediaKey; After: regenerated");
		}

		[Test]
		public void SettingsChanged_MalformedJson_IsRecordedVerbatim()
		{
			AuditQueueLogic.GetSettingsChangedAuditData("{\"Name\":", null)
				.Should().Be("Before: {\"Name\":; After: (none)");
		}

		[Test]
		public void SettingsChanged_NoPayloadOrNoDifference_IsNoData()
		{
			AuditQueueLogic.GetSettingsChangedAuditData(null, " ").Should().Be("No Data");
			AuditQueueLogic.GetSettingsChangedAuditData("{\"Name\":\"A\"}", "{\"Name\":\"A\"}").Should().Be("No Data");
		}
	}
}
