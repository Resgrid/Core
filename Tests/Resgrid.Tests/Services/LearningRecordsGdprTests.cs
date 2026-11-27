using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class GdprExportProtectedDataTests
	{
		[Test]
		public async Task Export_carries_the_members_own_transcript_voided_rows_included_and_nobody_elses()
		{
			var learningRecords = new Mock<ILearningRecordService>();
			learningRecords.Setup(x => x.GetTranscriptAsync(DeptId, UserId, null, null, null, null, true)).ReturnsAsync(new List<LearningRecord>
			{
				new LearningRecord { LearningRecordId = 1, DepartmentId = DeptId, UserId = UserId, SourceKind = (int)LearningRecordSourceKinds.CourseCompletion,
					Title = "Hazmat awareness", Hours = 1.5m, Status = (int)LearningRecordStatuses.Verified, CompletedOn = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc) },
				new LearningRecord { LearningRecordId = 2, DepartmentId = DeptId, UserId = UserId, SourceKind = (int)LearningRecordSourceKinds.ExternalSubmission,
					Title = "State EMT refresher", Hours = 4m, Status = (int)LearningRecordStatuses.Voided, VoidReason = "Duplicate", CompletedOn = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc) },
				// A row the repository should never return; the export filters it anyway.
				new LearningRecord { LearningRecordId = 3, DepartmentId = DeptId, UserId = "someone-else", Title = "FOREIGN-MEMBER-CANARY", Status = (int)LearningRecordStatuses.Verified },
				new LearningRecord { LearningRecordId = 4, DepartmentId = 999, UserId = UserId, Title = "FOREIGN-TENANT-CANARY", Status = (int)LearningRecordStatuses.Verified }
			});
			var checklistReminders = new Mock<IChecklistReminderRepository>();
			checklistReminders.SetReturnsDefault(Task.FromResult(new List<ChecklistReminder>()));
			_service = new GdprDataExportService(_repository.Object, _userProfileService.Object,
				_memberSensitiveDataService.Object, _emergencyContactService.Object, _usersService.Object,
				_departmentsService.Object, _departmentGroupsService.Object, _personnelRolesService.Object,
				_actionLogsService.Object, _messageService.Object, _certificationService.Object,
				_trainingService.Object, _shiftsService.Object, _emailService.Object, new ChecklistWorkflowTests.MemoryStore(),
				new Lazy<IReadinessHistoryProtectionService>(() => new ReadinessHistoryProtectionService(Mock.Of<IProtectedWriteService>(), Mock.Of<IDepartmentDataProtectionService>())),
				checklistReminders.Object, EmptyWorkOrders(), EmptyInventory(), EmptyMaintenance(), Mock.Of<IMemberWellnessService>(), learningRecords.Object, Mock.Of<IMemberHealthService>(), Array.Empty<IGdprExportSection>());

			var json = (await RunExportAsync())["learning_records.json"];

			json.Should().NotContain("CANARY");
			var rows = JArray.Parse(json);
			rows.Select(r => r["LearningRecordId"].Value<int>()).Should().BeEquivalentTo(new[] { 1, 2 });
			rows.Single(r => r["LearningRecordId"].Value<int>() == 1)["Source"].Value<string>().Should().Be("CourseCompletion");
			var voided = rows.Single(r => r["LearningRecordId"].Value<int>() == 2);
			voided["Status"].Value<string>().Should().Be("Voided");
			voided["VoidReason"].Value<string>().Should().Be("Duplicate");
		}
	}
}
