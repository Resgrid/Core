using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class GdprExportProtectedDataTests
	{
		/// <summary>
		/// Round 5F: the LMS section of the member's GDPR export (enrollments, attempts without keys, requirement status rows, training requests,
		/// protocol quiz attempts and micro-learning answers): the member's own rows only, nothing that reveals an answer key.
		/// </summary>
		private static LmsGdprExportSection LmsSection()
		{
			var enrollments = new Mock<ILmsEnrollmentsRepository>();
			enrollments.Setup(x => x.GetForUserAsync(DeptId, UserId)).ReturnsAsync(new List<LmsEnrollment>
			{
				new LmsEnrollment { LmsEnrollmentId = 1, DepartmentId = DeptId, UserId = UserId, LmsCourseId = 7, LmsCourseVersionId = 70, Source = (int)LmsEnrollmentSources.Assigned,
					Status = (int)LmsEnrollmentStatuses.Completed, AssignedOn = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) },
				new LmsEnrollment { LmsEnrollmentId = 2, DepartmentId = DeptId, UserId = "someone-else", LmsCourseId = 7, Status = (int)LmsEnrollmentStatuses.Completed, RolePathKey = "FOREIGN-MEMBER-CANARY" }
			});
			var courses = new Mock<ILmsCoursesRepository>();
			courses.Setup(x => x.GetVisibleByIdsAsync(DeptId, It.IsAny<IEnumerable<int>>())).ReturnsAsync(new List<LmsCourse> { new LmsCourse { LmsCourseId = 7, DepartmentId = DeptId, Title = "Hazmat operations" } });
			var attempts = new Mock<ILmsAssessmentAttemptsRepository>();
			attempts.Setup(x => x.GetForUserWithoutSnapshotAsync(DeptId, UserId)).ReturnsAsync(new List<LmsAssessmentAttempt>
			{
				new LmsAssessmentAttempt
				{
					LmsAssessmentAttemptId = 11, DepartmentId = DeptId, UserId = UserId, LmsCourseVersionId = 70, LmsLessonId = 700, AttemptNumber = 1, Status = (int)LmsAttemptStatuses.Scored,
					Percent = 80, Passed = true, PassMark = 80,
					// Whatever a repository returned, the export never writes the snapshot (the drawn questions' key).
					SnapshotJson = "{\"Questions\":[{\"QuestionId\":1,\"CorrectKeys\":[\"SECRET-KEY\"],\"Explanation\":\"SECRET-EXPLANATION\"}]}",
					AnswersJson = JsonConvert.SerializeObject(new[] { new LmsAttemptAnswer { QuestionId = 1, Keys = new List<string> { "b" } } }),
					ResultsJson = JsonConvert.SerializeObject(new[] { new LmsQuestionResult { QuestionId = 1, Correct = true, Awarded = 1, Points = 1 } })
				}
			});
			var requirements = new Mock<ILmsRequirementsRepository>();
			requirements.Setup(x => x.GetForDepartmentAsync(DeptId, true)).ReturnsAsync(new List<LmsRequirement> { new LmsRequirement { LmsRequirementId = 5, DepartmentId = DeptId, Name = "Annual EVOC" } });
			var members = new Mock<ILmsRequirementMembersRepository>();
			members.Setup(x => x.GetOpenForUserAsync(DeptId, UserId)).ReturnsAsync(new List<LmsRequirementMember>
			{
				new LmsRequirementMember { LmsRequirementMemberId = 50, DepartmentId = DeptId, LmsRequirementId = 5, UserId = UserId, State = (int)LmsRequirementStates.Overdue },
				new LmsRequirementMember { LmsRequirementMemberId = 51, DepartmentId = 999, LmsRequirementId = 5, UserId = UserId, ExemptReason = "FOREIGN-TENANT-CANARY" }
			});
			var requests = new Mock<ILmsTrainingRequestsRepository>();
			requests.Setup(x => x.GetForUserAsync(DeptId, UserId)).ReturnsAsync(new List<LmsTrainingRequest>
			{
				new LmsTrainingRequest { LmsTrainingRequestId = 60, DepartmentId = DeptId, UserId = UserId, Title = "Fire Officer I", Status = 0, SubmittedOn = DateTime.UtcNow }
			});
			var quiz = new Mock<ILmsProtocolQuizAttemptsRepository>();
			quiz.Setup(x => x.GetForUserAsync(DeptId, UserId)).ReturnsAsync(new List<LmsProtocolQuizAttempt>
			{
				new LmsProtocolQuizAttempt { LmsProtocolQuizAttemptId = 80, DepartmentId = DeptId, UserId = UserId, DispatchProtocolId = 3, ProtocolVersionId = 30, Passed = false, Percent = 40,
					Status = (int)LmsProtocolQuizAttemptStatuses.Scored, SnapshotJson = "{\"Questions\":[{\"CorrectKeys\":[\"SECRET-QUIZ-KEY\"]}]}" }
			});
			var micro = new Mock<ILmsMicroLearningRepository>();
			micro.Setup(x => x.GetForUserAsync(DeptId, UserId, 0)).ReturnsAsync(new List<LmsMicroLearningItem>
			{
				new LmsMicroLearningItem { LmsMicroLearningItemId = 90, DepartmentId = DeptId, UserId = UserId, DispatchProtocolId = 3, WeekKey = "2026-W48",
					SnapshotJson = "{\"Questions\":[{\"QuestionId\":4,\"Text\":\"Which channel for a mayday?\",\"CorrectKeys\":[\"SECRET-MICRO-KEY\"],\"Explanation\":\"SECRET-WHY\"}]}",
					AnsweredOn = DateTime.UtcNow, AnswersJson = "[\"a\"]", Correct = false }
			});
			return new LmsGdprExportSection(enrollments.Object, attempts.Object, courses.Object, requirements.Object, members.Object, requests.Object, quiz.Object, micro.Object);
		}

		[Test]
		public async Task The_lms_section_carries_the_members_own_learning_rows_and_never_an_answer_key()
		{
			var section = LmsSection();
			section.EntryName.Should().Be("lms_learning.json");

			var json = JsonConvert.SerializeObject(await section.BuildAsync(UserId, DeptId));

			json.Should().NotContain("SECRET").And.NotContain("CANARY");
			var data = JObject.Parse(json);
			data["Enrollments"].Should().ContainSingle();
			data["Enrollments"][0]["Course"].Value<string>().Should().Be("Hazmat operations");
			data["AssessmentAttempts"][0]["Answers"][0]["Keys"][0].Value<string>().Should().Be("b", "the member's own answer");
			data["AssessmentAttempts"][0]["Results"][0]["Correct"].Value<bool>().Should().BeTrue();
			data["RequirementStatus"].Should().ContainSingle().Which["Requirement"].Value<string>().Should().Be("Annual EVOC");
			data["RequirementStatus"][0]["State"].Value<string>().Should().Be("Overdue");
			data["TrainingRequests"][0]["Title"].Value<string>().Should().Be("Fire Officer I");
			data["ProtocolQuizAttempts"].Should().ContainSingle();
			data["MicroLearning"][0]["Question"].Value<string>().Should().Be("Which channel for a mayday?");
		}

		[Test]
		public async Task Registered_sections_are_written_into_the_export_archive()
		{
			var checklistReminders = new Mock<IChecklistReminderRepository>();
			checklistReminders.SetReturnsDefault(Task.FromResult(new List<ChecklistReminder>()));
			_service = new GdprDataExportService(_repository.Object, _userProfileService.Object,
				_memberSensitiveDataService.Object, _emergencyContactService.Object, _usersService.Object,
				_departmentsService.Object, _departmentGroupsService.Object, _personnelRolesService.Object,
				_actionLogsService.Object, _messageService.Object, _certificationService.Object,
				_trainingService.Object, _shiftsService.Object, _emailService.Object, new ChecklistWorkflowTests.MemoryStore(),
				new Lazy<IReadinessHistoryProtectionService>(() => new ReadinessHistoryProtectionService(Mock.Of<IProtectedWriteService>(), Mock.Of<IDepartmentDataProtectionService>())),
				checklistReminders.Object, EmptyWorkOrders(), EmptyInventory(), EmptyMaintenance(), Mock.Of<IMemberWellnessService>(), Mock.Of<ILearningRecordService>(),
				Mock.Of<IMemberHealthService>(), new IGdprExportSection[] { LmsSection() });

			var files = await RunExportAsync();

			files.Should().ContainKey("lms_learning.json");
			files["lms_learning.json"].Should().Contain("Hazmat operations").And.NotContain("SECRET");
		}
	}
}
