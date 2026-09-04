using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>Notification EventTypes 31 (RecordReturnedForCorrection): author-targeted, header-only content.</summary>
	[TestFixture]
	public class RecordsNotificationServiceTests
	{
		private const int Dept = 7;
		private Mock<IRmsOperationalRecordsRepository> _records;
		private Mock<ICommunicationService> _communication;
		private Mock<IUserProfileService> _profiles;
		private RecordsNotificationService _service;

		[SetUp]
		public void SetUp()
		{
			_records = new Mock<IRmsOperationalRecordsRepository>();
			_communication = new Mock<ICommunicationService>();
			_communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()))
				.ReturnsAsync(true);
			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetProfileByUserIdAsync("chief", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "chief", FirstName = "Casey", LastName = "Chief" });
			_profiles.Setup(p => p.GetProfileByUserIdAsync("author", It.IsAny<bool>())).ReturnsAsync(new UserProfile { UserId = "author", FirstName = "Alex", LastName = "Author" });

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Test FD" });
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetTextToCallNumberForDepartmentAsync(Dept)).ReturnsAsync("+15555550100");

			_service = new RecordsNotificationService(_records.Object, _communication.Object, departments.Object, settings.Object, _profiles.Object, new Mock<IRmsIncidentReportsRepository>().Object);
		}

		private static RmsOperationalRecord Returned() => new RmsOperationalRecord
		{
			RmsOperationalRecordId = "rec-1",
			DepartmentId = Dept,
			DefinitionKey = RmsDefinitionKeys.Training,
			RecordType = (int)RmsOperationalRecordType.Training,
			State = (int)RmsRecordState.Returned,
			DraftReference = "D-7Q2MX",
			AuthorUserId = "author",
			ReviewerUserId = "chief",
			ReturnReasonCode = "incomplete",
			ReturnReasonText = "Add the roster and the evolutions run."
		};

		[Test]
		public async Task Sends_the_author_a_header_only_message_with_reviewer_reason_and_link()
		{
			_records.Setup(r => r.GetByIdForDepartmentAsync(Dept, "rec-1")).ReturnsAsync(Returned());

			var sent = await _service.NotifyReturnedForCorrectionAsync(Dept, "rec-1");

			sent.Should().BeTrue();
			_communication.Verify(c => c.SendNotificationAsync("author", Dept,
				It.Is<string>(m => m.Contains("Training record D-7Q2MX") && m.Contains("Casey Chief") && m.Contains("Reason: incomplete") && m.Contains("Add the roster") && m.Contains("/User/Records/Details/rec-1")),
				"+15555550100", It.Is<Department>(d => d.DepartmentId == Dept), RecordsNotificationService.ReturnedForCorrectionTitle,
				It.Is<UserProfile>(p => p.UserId == "author"), false), Times.Once);
		}

		[Test]
		public async Task Does_nothing_when_the_record_is_missing_or_not_returned()
		{
			_records.Setup(r => r.GetByIdForDepartmentAsync(Dept, "missing")).ReturnsAsync((RmsOperationalRecord)null);
			var draft = Returned();
			draft.State = (int)RmsRecordState.Draft;
			_records.Setup(r => r.GetByIdForDepartmentAsync(Dept, "rec-1")).ReturnsAsync(draft);

			(await _service.NotifyReturnedForCorrectionAsync(Dept, "missing")).Should().BeFalse();
			(await _service.NotifyReturnedForCorrectionAsync(Dept, "rec-1")).Should().BeFalse();
			(await _service.NotifyReturnedForCorrectionAsync(0, "rec-1")).Should().BeFalse();

			_communication.Verify(c => c.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public void Message_uses_the_record_number_once_assigned_and_caps_the_reviewer_note()
		{
			var record = Returned();
			record.RecordNumber = "TRN-2026-0001";
			record.ReturnReasonText = new string('x', 400);

			var message = RecordsNotificationService.BuildReturnedForCorrectionMessage(record, null);

			message.Should().StartWith("Training record TRN-2026-0001 was returned for correction.");
			message.Should().NotContain(" by ");
			message.Should().Contain(new string('x', 200) + "…").And.NotContain(new string('x', 201));
			message.Should().EndWith("/User/Records/Details/rec-1");
		}

		[Test]
		public async Task A_delivery_fault_is_logged_and_reported_as_not_sent()
		{
			_records.Setup(r => r.GetByIdForDepartmentAsync(Dept, "rec-1")).ReturnsAsync(Returned());
			_communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()))
				.ThrowsAsync(new System.InvalidOperationException("push down"));

			(await _service.NotifyReturnedForCorrectionAsync(Dept, "rec-1", CancellationToken.None)).Should().BeFalse();
		}
	}
}
