using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Models.v4.Trainings;
using Resgrid.Web.ServicesCore.Helpers;
using ApiTrainingsController = Resgrid.Web.Services.Controllers.v4.TrainingsController;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// v4 Trainings (Training T0 item 3): the answer key never leaves the server (R-T11), the Trainings module switch refuses every
	/// endpoint, a member opens only what is assigned, open to all or managed by them, the report needs the assign right, and a
	/// quiz submission counts each question once.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class TrainingsApiTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "member-user";

		private Mock<ITrainingService> _trainings;
		private Mock<ITrainingAssignmentService> _assignments;
		private Mock<IMyTrainingService> _myTraining;
		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentSettingsService> _settings;
		private DepartmentModuleSettings _modules;
		private TrainingManagementScope _scope;
		private ApiTrainingsController _controller;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_trainings = new Mock<ITrainingService>();
			_assignments = new Mock<ITrainingAssignmentService>();
			_myTraining = new Mock<IMyTrainingService>();
			_authorization = new Mock<IAuthorizationService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_modules = new DepartmentModuleSettings();
			_scope = TrainingManagementScope.None(UserId, DepartmentId);

			_settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _modules);
			_authorization.Setup(x => x.GetTrainingManagementScopeAsync(UserId, DepartmentId)).ReturnsAsync(() => _scope);
			_trainings.Setup(x => x.GetAssignmentsForUserAsync(DepartmentId, UserId, It.IsAny<DateTime>())).ReturnsAsync(new List<TrainingAssignmentStatus>());

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("TrainingsApiTests").Start();

			_controller = new ApiTrainingsController(_trainings.Object, _assignments.Object, _myTraining.Object, _authorization.Object, _settings.Object, new Mock<ILearningRecordService>().Object,
				Mock.Of<ITrainingCompletionRecordingService>(), Mock.Of<ITrainingSubmissionService>(), Mock.Of<ITrainingRecordDocumentService>())
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private static Training QuizTraining(int id, string createdBy, bool openToAll, params string[] assignees) => new Training
		{
			TrainingId = id,
			DepartmentId = DepartmentId,
			CreatedByUserId = createdBy,
			Name = "Hose Testing",
			Description = "<p>Annual hose testing</p>",
			TrainingText = "<p>Read this</p><iframe src=\"https://evil.example.com/x\"></iframe>",
			MinimumScore = 80,
			CreatedOn = DateTime.UtcNow.AddDays(-1),
			OpenToAllMembers = openToAll,
			Users = assignees.Select((u, i) => new TrainingUser { TrainingUserId = i + 1, TrainingId = id, UserId = u }).ToList(),
			Attachments = new List<TrainingAttachment> { new TrainingAttachment { TrainingAttachmentId = 5, TrainingId = id, FileName = "sop.pdf", FileType = "application/pdf", FileSize = 1234 } },
			Questions = new List<TrainingQuestion>
			{
				new TrainingQuestion
				{
					TrainingQuestionId = 1, TrainingId = id, Question = "Test pressure?", Type = (int)TrainingQuestionTypes.SingleChoice,
					Answers = new List<TrainingQuestionAnswer>
					{
						new TrainingQuestionAnswer { TrainingQuestionAnswerId = 11, TrainingQuestionId = 1, Answer = "250 psi", Correct = true },
						new TrainingQuestionAnswer { TrainingQuestionAnswerId = 12, TrainingQuestionId = 1, Answer = "50 psi" }
					}
				}
			}
		};

		[Test]
		public void No_training_payload_type_carries_an_answer_key()
		{
			var forbidden = new[] { "Correct", "IsCorrect", "AnswerKey", "CorrectOptionIds", "AnsweredCorrectly", "Snapshot", "ChosenOptionIds" };
			var types = typeof(TrainingQuizData).Assembly.GetTypes().Where(t => t.Namespace == typeof(TrainingQuizData).Namespace).ToList();

			types.Should().Contain(typeof(TrainingQuizOptionData));
			types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => t.Name + "." + p.Name))
				.Where(name => forbidden.Contains(name.Substring(name.IndexOf('.') + 1)))
				.Should().BeEmpty("quiz payloads carry questions and options only (R-T11)");
		}

		[Test]
		public async Task An_assignee_reads_the_training_with_the_quiz_as_questions_and_options_only()
		{
			_trainings.Setup(x => x.GetTrainingByIdAsync(1)).ReturnsAsync(QuizTraining(1, "officer", false, UserId));

			var response = await _controller.GetTraining(1, CancellationToken.None);

			var data = response.Value.Data;
			data.Quiz.Should().NotBeNull();
			data.Quiz.Questions.Single().Options.Select(o => o.Text).Should().Equal("250 psi", "50 psi");
			data.Attachments.Single().FileName.Should().Be("sop.pdf");
			data.TrainingText.Should().NotContain("evil.example.com", "only allow-listed embeds survive");

			var json = JsonConvert.SerializeObject(response.Value);
			json.Should().NotContain("\"Correct\"");
			json.Should().NotContain("Correct\":true");
			_trainings.Verify(x => x.SetTrainingAsViewedAsync(1, UserId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_training_open_to_all_members_is_read_without_its_quiz_by_someone_not_assigned()
		{
			_trainings.Setup(x => x.GetTrainingByIdAsync(2)).ReturnsAsync(QuizTraining(2, "officer", true, "someone-else"));

			var response = await _controller.GetTraining(2, CancellationToken.None);

			response.Value.Data.Should().NotBeNull();
			response.Value.Data.Quiz.Should().BeNull("an open training shows its content, not its quiz, to non-assignees");
			response.Value.Data.Assignment.Should().BeNull();
		}

		[Test]
		public async Task A_member_not_assigned_cannot_open_a_training_that_is_not_open_to_all()
		{
			_trainings.Setup(x => x.GetTrainingByIdAsync(3)).ReturnsAsync(QuizTraining(3, "officer", false, "someone-else"));
			_trainings.Setup(x => x.GetTrainingRowByIdAsync(3)).ReturnsAsync(QuizTraining(3, "officer", false, "someone-else"));
			_trainings.Setup(x => x.GetTrainingAttachmentByIdAsync(5)).ReturnsAsync(new TrainingAttachment { TrainingAttachmentId = 5, TrainingId = 3 });

			(await _controller.GetTraining(3, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
			(await _controller.GetTrainingAttachment(5)).Should().BeOfType<UnauthorizedResult>();

			_trainings.Verify(x => x.SetTrainingAsViewedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_trainings.Verify(x => x.GetTrainingAttachmentWithDataAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_training_of_another_department_is_refused()
		{
			var foreign = QuizTraining(4, "officer", true, UserId);
			foreign.DepartmentId = DepartmentId + 1;
			_trainings.Setup(x => x.GetTrainingByIdAsync(4)).ReturnsAsync(foreign);

			(await _controller.GetTraining(4, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();
		}

		[Test]
		public async Task Every_endpoint_is_refused_while_the_trainings_module_is_switched_off()
		{
			_modules.TrainingDisabled = true;
			_scope.Author = new TrainingRightScope { ManagesAll = true };

			(await _controller.GetMyTrainings()).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.GetTraining(1, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.GetTrainingAttachment(5)).Should().BeOfType<NotFoundResult>();
			(await _controller.SubmitQuiz(new SubmitTrainingQuizInput { TrainingId = 1 }, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.GetMyAttempts(1)).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.SelfEnroll(new TrainingIdInput { TrainingId = 1 }, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.FlagQuestion(new FlagTrainingQuestionInput { TrainingId = 1, QuestionId = 1 }, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.PinCertification(new PinTrainingCertificationInput { CertificationId = 1 }, CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.GetTrainingReport(1)).Result.Should().BeOfType<NotFoundResult>();

			_trainings.Invocations.Should().BeEmpty();
			_myTraining.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task The_report_is_refused_without_the_assign_right_for_that_training()
		{
			var training = QuizTraining(6, "officer", false, UserId);
			_trainings.Setup(x => x.GetTrainingRowByIdAsync(6)).ReturnsAsync(training);
			// Authoring every training does not admit the report: assign-and-report is its own right (permission 126).
			_scope.Author = new TrainingRightScope { ManagesAll = true };
			_scope.Assign = new TrainingRightScope();

			(await _controller.GetTrainingReport(6)).Result.Should().BeOfType<UnauthorizedResult>();
			_trainings.Verify(x => x.GetTrainingReportAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Never);

			_scope.Assign = new TrainingRightScope { ManagesAll = true };
			_trainings.Setup(x => x.GetTrainingReportAsync(DepartmentId, 6, It.IsAny<DateTime>())).ReturnsAsync(new TrainingReport
			{
				Training = training,
				CompleteCount = 1,
				CompletionPercent = 100,
				Rows = new List<TrainingReportRow>
				{
					new TrainingReportRow { UserId = UserId, Name = "Member", State = TrainingAssignmentStates.Complete, IsActiveMember = true, Assignment = training.Users.First(), AttemptCount = 2,
						LastAttempt = new TrainingQuizAttempt { Score = 90, SubmittedOn = DateTime.UtcNow, Snapshot = "{\"secret\":true}" } }
				}
			});

			var report = await _controller.GetTrainingReport(6);

			report.Value.Data.Rows.Single().StateName.Should().Be("Complete");
			report.Value.Data.Rows.Single().LastAttemptScore.Should().Be(90);
			JsonConvert.SerializeObject(report.Value).Should().NotContain("secret", "per-question answers are not part of the report (R-T12)");
		}

		[Test]
		public void Posted_answers_count_each_question_once_and_are_capped()
		{
			var answers = ApiTrainingsController.ToAnswers(new[]
			{
				new TrainingQuizAnswerInput { QuestionId = 1, OptionIds = new List<int> { 11 } },
				new TrainingQuizAnswerInput { QuestionId = 1, OptionIds = new List<int> { 12 } },
				new TrainingQuizAnswerInput { QuestionId = 2, OptionIds = new List<int> { 21, 21, -4, 0 } },
				new TrainingQuizAnswerInput { QuestionId = -3, OptionIds = new List<int> { 31 } },
				null
			});

			answers.Keys.Should().BeEquivalentTo(new[] { 1, 2 });
			answers[1].Should().Equal(11);
			answers[2].Should().Equal(21);

			ApiTrainingsController.ToAnswers(Enumerable.Range(1, 150).Select(i => new TrainingQuizAnswerInput { QuestionId = i, OptionIds = new List<int> { i } }))
				.Count.Should().Be(TrainingQuizScorer.MaxQuestions);
		}

		[Test]
		public async Task A_submission_is_scored_on_the_server_and_returns_the_result_but_not_the_key()
		{
			IReadOnlyDictionary<int, List<int>> posted = null;
			_trainings.Setup(x => x.RecordQuizAttemptAsync(DepartmentId, 1, UserId, It.IsAny<IReadOnlyDictionary<int, List<int>>>(), It.IsAny<CancellationToken>()))
				.Callback((int d, int t, string u, IReadOnlyDictionary<int, List<int>> a, CancellationToken c) => posted = a)
				.ReturnsAsync(new TrainingQuizSubmission
				{
					Status = TrainingQuizSubmissionStatuses.Recorded,
					Attempt = new TrainingQuizAttempt { AttemptNumber = 2, Snapshot = "{\"Correct\":true}" },
					Result = new TrainingQuizResult { Score = 50, Passed = false, QuestionCount = 2, CorrectCount = 1, Snapshot = new TrainingQuizSnapshot() },
					AttemptsLeft = 1
				});

			var response = await _controller.SubmitQuiz(new SubmitTrainingQuizInput
			{
				TrainingId = 1,
				Answers = new List<TrainingQuizAnswerInput>
				{
					new TrainingQuizAnswerInput { QuestionId = 7, OptionIds = new List<int> { 71 } },
					new TrainingQuizAnswerInput { QuestionId = 7, OptionIds = new List<int> { 72 } }
				}
			}, CancellationToken.None);

			posted.Should().ContainKey(7).WhoseValue.Should().Equal(71);
			posted.Count.Should().Be(1);
			response.Value.Data.Recorded.Should().BeTrue();
			response.Value.Data.Score.Should().Be(50);
			response.Value.Data.AttemptsLeft.Should().Be(1);
			JsonConvert.SerializeObject(response.Value).Should().NotContain("\"Correct\"");
		}

		[Test]
		public async Task A_submission_from_someone_not_assigned_is_refused_and_a_limit_is_reported()
		{
			_trainings.Setup(x => x.RecordQuizAttemptAsync(DepartmentId, 1, UserId, It.IsAny<IReadOnlyDictionary<int, List<int>>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.NotAssigned });
			(await _controller.SubmitQuiz(new SubmitTrainingQuizInput { TrainingId = 1 }, CancellationToken.None)).Result.Should().BeOfType<UnauthorizedResult>();

			_trainings.Setup(x => x.RecordQuizAttemptAsync(DepartmentId, 1, UserId, It.IsAny<IReadOnlyDictionary<int, List<int>>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.AttemptLimitReached, AttemptsLeft = 0 });
			var limited = await _controller.SubmitQuiz(new SubmitTrainingQuizInput { TrainingId = 1 }, CancellationToken.None);

			var bad = limited.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
			((TrainingQuizSubmissionResult)bad.Value).Data.Status.Should().Be("AttemptLimitReached");
		}

		[Test]
		public async Task My_attempts_show_results_only()
		{
			_trainings.Setup(x => x.GetTrainingRowByIdAsync(1)).ReturnsAsync(QuizTraining(1, "officer", false, UserId));
			_trainings.Setup(x => x.GetAttemptsForMemberAsync(DepartmentId, 1, UserId)).ReturnsAsync(new List<TrainingQuizAttempt>
			{
				new TrainingQuizAttempt { UserId = UserId, AttemptNumber = 1, Score = 40, Passed = false, Snapshot = "{\"ChosenOptionIds\":[12]}" },
				new TrainingQuizAttempt { UserId = UserId, AttemptNumber = 2, Score = 100, Passed = true, Snapshot = "{\"ChosenOptionIds\":[11]}" }
			});

			var response = await _controller.GetMyAttempts(1);

			response.Value.Data.Select(a => a.AttemptNumber).Should().Equal(2, 1);
			JsonConvert.SerializeObject(response.Value).Should().NotContain("ChosenOptionIds");
		}

		[Test]
		public async Task My_trainings_lists_assignments_catalog_and_certifications_due()
		{
			var now = DateTime.UtcNow;
			var overdue = new TrainingAssignmentStatus
			{
				Training = new Training { TrainingId = 1, DepartmentId = DepartmentId, Name = "Overdue", QuizAttemptLimit = 3, QuestionCount = 2 },
				Assignment = new TrainingUser { UserId = UserId, AttemptCount = 1 },
				DueUtc = now.AddDays(-1),
				State = TrainingAssignmentStates.Overdue
			};
			_myTraining.Setup(x => x.GetOverviewAsync(DepartmentId, UserId, It.IsAny<DateTime>())).ReturnsAsync(new MyTrainingOverview
			{
				Overdue = new List<TrainingAssignmentStatus> { overdue },
				Catalog = new List<Training> { new Training { TrainingId = 9, DepartmentId = DepartmentId, Name = "Reference", OpenToAllMembers = true, AllowSelfEnroll = true } },
				Certifications = new List<MyTrainingCertification>
				{
					new MyTrainingCertification { Certification = new PersonnelCertification { PersonnelCertificationId = 4 }, Name = "EMT", ExpiresOn = now.AddDays(10), DueSoon = true, DaysLeft = 10 }
				}
			});

			var response = await _controller.GetMyTrainings();

			var data = response.Value.Data;
			data.Assignments.Single().StateName.Should().Be("Overdue");
			data.Assignments.Single().AttemptsLeft.Should().Be(2);
			data.Assignments.Single().HasQuiz.Should().BeTrue();
			data.Catalog.Single().CanSelfEnroll.Should().BeTrue();
			data.Certifications.Single().CertificationId.Should().Be(4);
		}
	}
}
