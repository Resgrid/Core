using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Training;
using TrainingsResources = Resgrid.Localization.Areas.User.Trainings.Trainings;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The Trainings pages (T0): a training is opened by its assignees, by whoever manages it and, when open, by every member;
	/// authoring, assigning-and-reporting and deleting are separate rights (126, 127); the module switch closes every page;
	/// the quiz reaches the browser without its answer key and is scored on the server from validated posted ids.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class TrainingsControllerAccessTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "member";

		private Mock<ITrainingService> _trainingService;
		private Mock<IAuthorizationService> _authorizationService;
		private Mock<ITrainingAssignmentService> _assignmentService;
		private Mock<IMyTrainingService> _myTrainingService;
		private Mock<IMemberLeaveService> _leaveService;
		private Mock<IDepartmentSettingsService> _departmentSettings;
		private TrainingsController _controller;

		[SetUp]
		public void SetUp()
		{
			_trainingService = new Mock<ITrainingService>();
			_authorizationService = new Mock<IAuthorizationService>();
			_assignmentService = new Mock<ITrainingAssignmentService>();
			_myTrainingService = new Mock<IMyTrainingService>();
			_leaveService = new Mock<IMemberLeaveService>();
			_departmentSettings = new Mock<IDepartmentSettingsService>();

			// A member with no training rights: they open what is assigned to them and manage nothing.
			ScopeIs(TrainingManagementScope.None(UserId, DepartmentId));

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor =
				new HttpContextAccessor { HttpContext = httpContext };

			var localizer = new Mock<IStringLocalizer<TrainingsResources>>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));

			_controller = new TrainingsController(
				Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IDepartmentsService>(),
				_trainingService.Object,
				Mock.Of<IPersonnelRolesService>(),
				_authorizationService.Object,
				_assignmentService.Object,
				_myTrainingService.Object,
				_leaveService.Object,
				_departmentSettings.Object,
				Mock.Of<ICertificationService>(),
				Mock.Of<IUnitsService>(),
				Mock.Of<IShiftsService>(),
				Mock.Of<ILearningRecordService>(),
				localizer.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private void ScopeIs(TrainingManagementScope scope) =>
			_authorizationService.Setup(x => x.GetTrainingManagementScopeAsync(UserId, DepartmentId)).ReturnsAsync(scope);

		private static Training TrainingFor(int id, string createdBy, params string[] assignees) => new Training
		{
			TrainingId = id,
			DepartmentId = DepartmentId,
			Name = "Training " + id,
			CreatedByUserId = createdBy,
			Users = assignees.Select((x, i) => new TrainingUser { TrainingUserId = id * 100 + i, TrainingId = id, UserId = x }).ToList(),
			Questions = new List<TrainingQuestion>(),
			Attachments = new List<TrainingAttachment>()
		};

		private static Training QuizTraining(int id, params string[] assignees)
		{
			var training = TrainingFor(id, "officer", assignees);
			training.Questions = new List<TrainingQuestion>
			{
				new TrainingQuestion
				{
					TrainingQuestionId = 5, TrainingId = id, Question = "Pump pressure?", Type = (int)TrainingQuestionTypes.SingleChoice,
					Answers = new List<TrainingQuestionAnswer>
					{
						new TrainingQuestionAnswer { TrainingQuestionAnswerId = 51, TrainingQuestionId = 5, Answer = "150 psi", Correct = true },
						new TrainingQuestionAnswer { TrainingQuestionAnswerId = 52, TrainingQuestionId = 5, Answer = "50 psi" }
					}
				}
			};
			return training;
		}

		private static IFormCollection Form(params (string Key, string Value)[] values) =>
			new FormCollection(values.GroupBy(v => v.Key).ToDictionary(g => g.Key, g => new StringValues(g.Select(v => v.Value).ToArray())));

		#region Opening trainings

		[Test]
		public async Task Index_shows_the_members_own_training_overview()
		{
			var overview = new MyTrainingOverview();
			overview.Overdue.Add(new TrainingAssignmentStatus { Training = TrainingFor(1, "officer", UserId), Assignment = new TrainingUser { UserId = UserId }, State = TrainingAssignmentStates.Overdue });
			_myTrainingService.Setup(x => x.GetOverviewAsync(DepartmentId, UserId, It.IsAny<DateTime>())).ReturnsAsync(overview);

			var result = await _controller.Index();

			var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<TrainingIndexModel>().Subject;
			model.MyTraining.Overdue.Select(x => x.Training.TrainingId).Should().Equal(1);
		}

		[Test]
		public async Task The_officer_list_is_refused_to_a_member_without_training_rights()
		{
			(await _controller.Manage()).Should().NotBeOfType<ViewResult>();
			_trainingService.Verify(x => x.GetAllTrainingsForDepartmentAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task The_officer_list_shows_only_the_trainings_the_member_manages()
		{
			ScopeIs(new TrainingManagementScope { UserId = UserId, DepartmentId = DepartmentId, ManagesOwn = true });
			_trainingService.Setup(x => x.GetAllTrainingsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Training>
			{
				TrainingFor(1, UserId, "someone"),
				TrainingFor(2, "officer", UserId)
			});

			var result = await _controller.Manage();

			var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<TrainingManageModel>().Subject;
			model.Rows.Select(x => x.Training.TrainingId).Should().Equal(1);
		}

		[Test]
		public async Task The_quiz_of_an_open_training_is_refused_to_a_member_it_was_not_assigned_to()
		{
			var open = QuizTraining(4, "someone-else");
			open.OpenToAllMembers = true;
			_trainingService.Setup(x => x.GetTrainingByIdAsync(4)).ReturnsAsync(open);

			(await _controller.Quiz(4, CancellationToken.None)).Should().NotBeOfType<ViewResult>();
		}

		[Test]
		public async Task An_attachment_of_an_open_training_is_served_to_any_member_from_the_one_download_path()
		{
			var open = TrainingFor(4, "officer", "someone-else");
			open.OpenToAllMembers = true;
			_trainingService.Setup(x => x.GetTrainingAttachmentByIdAsync(8)).ReturnsAsync(new TrainingAttachment { TrainingAttachmentId = 8, TrainingId = 4, FileName = "sog.pdf", FileType = "application/pdf" });
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(4)).ReturnsAsync(open);
			_trainingService.Setup(x => x.GetTrainingAttachmentWithDataAsync(8)).ReturnsAsync(new TrainingAttachment { TrainingAttachmentId = 8, TrainingId = 4, FileName = "sog.pdf", FileType = "application/pdf", Data = new byte[] { 1, 2, 3 } });

			var result = await _controller.GetTrainingAttachment(8);

			result.Should().BeOfType<FileContentResult>().Which.FileContents.Should().Equal(1, 2, 3);
		}

		[Test]
		public async Task An_attachment_of_a_training_that_is_not_assigned_to_the_member_is_not_served()
		{
			_trainingService.Setup(x => x.GetTrainingAttachmentByIdAsync(7)).ReturnsAsync(new TrainingAttachment { TrainingAttachmentId = 7, TrainingId = 2, FileName = "exam.pdf" });
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(2)).ReturnsAsync(TrainingFor(2, "officer", "someone-else"));

			(await _controller.GetTrainingAttachment(7)).Should().NotBeOfType<FileContentResult>();
			_trainingService.Verify(x => x.GetTrainingAttachmentWithDataAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task View_refuses_a_training_that_is_not_assigned_to_the_member()
		{
			_trainingService.Setup(x => x.GetTrainingByIdAsync(2)).ReturnsAsync(TrainingFor(2, "officer", "someone-else"));

			var result = await _controller.View(2);

			result.Should().NotBeOfType<ViewResult>();
			_trainingService.Verify(x => x.SetTrainingAsViewedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_scheduled_training_stays_hidden_from_its_assignee_until_it_starts()
		{
			var scheduled = TrainingFor(3, "officer", UserId);
			scheduled.StartsOn = DateTime.UtcNow.AddDays(2);
			_trainingService.Setup(x => x.GetTrainingByIdAsync(3)).ReturnsAsync(scheduled);

			(await _controller.View(3)).Should().NotBeOfType<ViewResult>();
		}

		[Test]
		public async Task Quiz_refuses_a_training_that_is_not_assigned_to_the_member()
		{
			_trainingService.Setup(x => x.GetTrainingByIdAsync(2)).ReturnsAsync(QuizTraining(2, "someone-else"));

			var result = await _controller.Quiz(2, CancellationToken.None);

			result.Should().NotBeOfType<ViewResult>();
			_trainingService.Verify(x => x.SetTrainingAsViewedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Opening trainings

		#region The quiz (R-T11, T-D4, T-D11, T-D23)

		[Test]
		public async Task The_quiz_page_carries_questions_and_options_but_never_the_answer_key()
		{
			_trainingService.Setup(x => x.GetTrainingByIdAsync(1)).ReturnsAsync(QuizTraining(1, UserId));

			var result = await _controller.Quiz(1, CancellationToken.None);

			var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<TrainingQuizView>().Subject;
			model.Questions.Should().ContainSingle().Which.Options.Select(o => o.OptionId).Should().Equal(51, 52);
			model.IsPreview.Should().BeFalse();
			Newtonsoft.Json.JsonConvert.SerializeObject(model).Should().NotContain("Correct").And.NotContain("true,");
		}

		[Test]
		public void No_quiz_view_model_type_has_a_property_that_could_carry_the_answer_key()
		{
			foreach (var type in new[] { typeof(TrainingQuizView), typeof(TrainingQuizQuestionView), typeof(TrainingQuizOptionView), typeof(TrainingQuizResultView) })
			{
				type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Should().NotContain(n => n.Contains("Correct") && n != "CorrectCount", type.Name);
				type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.PropertyType)
					.Should().NotContain(t => t == typeof(TrainingQuestionAnswer) || t == typeof(Training) || t == typeof(TrainingQuestion), type.Name);
			}
		}

		[Test]
		public async Task A_quiz_answer_from_someone_it_was_not_assigned_to_is_not_recorded()
		{
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(2)).ReturnsAsync(TrainingFor(2, "officer", "someone-else"));

			await _controller.SubmitQuiz(2, Form(("question_5", "51")), CancellationToken.None);

			_trainingService.Verify(x => x.RecordQuizAttemptAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<int, List<int>>>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Posted_answers_reach_the_service_once_per_question_with_malformed_keys_and_ids_dropped()
		{
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(1)).ReturnsAsync(TrainingFor(1, "officer", UserId));
			IReadOnlyDictionary<int, List<int>> recorded = null;
			_trainingService.Setup(x => x.RecordQuizAttemptAsync(DepartmentId, 1, UserId, It.IsAny<IReadOnlyDictionary<int, List<int>>>(), It.IsAny<CancellationToken>()))
				.Callback<int, int, string, IReadOnlyDictionary<int, List<int>>, CancellationToken>((_, _, _, answers, _) => recorded = answers)
				.ReturnsAsync(new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.Recorded, Result = new TrainingQuizResult { Score = 100, Passed = true, QuestionCount = 1, CorrectCount = 1 } });

			var result = await _controller.SubmitQuiz(1, Form(("question_5", "51"), ("question_05", "51"), ("question_+5", "51"), ("question_6", "abc"), ("question_7", "-3")), CancellationToken.None);

			result.Should().BeOfType<ViewResult>().Which.ViewName.Should().Be("QuizResult");
			recorded.Keys.Should().BeEquivalentTo(new[] { 5, 6, 7 });
			recorded[5].Should().Equal(51);
			recorded[6].Should().BeEmpty();
			recorded[7].Should().BeEmpty();
		}

		[Test]
		public async Task An_empty_or_missing_post_is_answered_without_an_error()
		{
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(1)).ReturnsAsync(TrainingFor(1, "officer", UserId));
			_trainingService.Setup(x => x.RecordQuizAttemptAsync(DepartmentId, 1, UserId, It.IsAny<IReadOnlyDictionary<int, List<int>>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.AttemptLimitReached });

			var result = await _controller.SubmitQuiz(1, null, CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
		}

		[Test]
		public async Task A_failed_attempt_offers_a_retake_while_attempts_remain()
		{
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(1)).ReturnsAsync(TrainingFor(1, "officer", UserId));
			_trainingService.Setup(x => x.RecordQuizAttemptAsync(DepartmentId, 1, UserId, It.IsAny<IReadOnlyDictionary<int, List<int>>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.Recorded, AttemptsLeft = 2, Result = new TrainingQuizResult { Score = 0, Passed = false, QuestionCount = 1 } });

			var result = await _controller.SubmitQuiz(1, Form(("question_5", "52")), CancellationToken.None);

			result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<TrainingQuizResultView>().Which.CanRetake.Should().BeTrue();
		}

		#endregion The quiz

		#region Rights (T0 item 1b)

		[Test]
		public async Task The_edit_page_with_its_answer_key_is_refused_to_a_member_who_does_not_manage_the_training()
		{
			// Assigned to the member, so they may open it - but not edit, report on, reset or delete it.
			_trainingService.Setup(x => x.GetTrainingByIdAsync(1)).ReturnsAsync(TrainingFor(1, "officer", UserId));
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(1)).ReturnsAsync(TrainingFor(1, "officer", UserId));

			(await _controller.Edit(1)).Should().NotBeOfType<ViewResult>();
			(await _controller.Report(1)).Should().NotBeOfType<ViewResult>();

			await _controller.DeleteTraining(1);
			await _controller.ResetUserTraining(1, "someone-else");

			_trainingService.Verify(x => x.DeleteTrainingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_assignmentService.Verify(x => x.ResetMemberAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_author_without_the_assign_right_edits_content_but_never_reaches_the_report_or_assignee_actions()
		{
			ScopeIs(new TrainingManagementScope
			{
				UserId = UserId, DepartmentId = DepartmentId,
				Author = new TrainingRightScope { ManagesAll = true, ManagesOwn = true },
				Assign = new TrainingRightScope(),
				Delete = new TrainingRightScope()
			});
			var training = QuizTraining(1, "someone");
			_trainingService.Setup(x => x.GetTrainingByIdAsync(1)).ReturnsAsync(training);
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(1)).ReturnsAsync(training);

			var edit = (await _controller.Edit(1)).Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<EditTrainingModel>().Subject;
			edit.CanAuthor.Should().BeTrue();
			edit.CanAssign.Should().BeFalse();

			(await _controller.Report(1)).Should().NotBeOfType<ViewResult>();
			await _controller.Extend(1, "someone", "2099-01-01", "Course cancelled");
			await _controller.RemoveAssignee(1, "someone");
			await _controller.Archive(1);

			_assignmentService.Verify(x => x.ExtendAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_assignmentService.Verify(x => x.RemoveAssigneeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_trainingService.Verify(x => x.ArchiveTrainingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_assigner_without_the_author_right_never_sees_the_answer_key_on_the_edit_page()
		{
			ScopeIs(new TrainingManagementScope
			{
				UserId = UserId, DepartmentId = DepartmentId,
				Author = new TrainingRightScope(),
				Assign = new TrainingRightScope { ManagesAll = true, ManagesOwn = true },
				Delete = new TrainingRightScope()
			});
			_trainingService.Setup(x => x.GetTrainingByIdAsync(1)).ReturnsAsync(QuizTraining(1, "someone"));

			var edit = (await _controller.Edit(1)).Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<EditTrainingModel>().Subject;

			edit.CanAuthor.Should().BeFalse();
			edit.Questions.Should().BeEmpty();
		}

		[Test]
		public async Task A_group_admin_may_extend_only_members_of_the_group_they_administer()
		{
			var group = new HashSet<string>(new[] { "in-group" }, StringComparer.OrdinalIgnoreCase);
			ScopeIs(new TrainingManagementScope
			{
				UserId = UserId, DepartmentId = DepartmentId,
				Author = new TrainingRightScope { ManagesOwn = true, AdministeredMemberUserIds = group },
				Assign = new TrainingRightScope { ManagesOwn = true, AdministeredMemberUserIds = group },
				Delete = new TrainingRightScope()
			});
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(1)).ReturnsAsync(TrainingFor(1, "officer", "in-group"));

			await _controller.Extend(1, "outsider", "2099-01-01", "Leave");
			await _controller.Extend(1, "in-group", "2099-01-01", "Leave");

			_assignmentService.Verify(x => x.ExtendAsync(DepartmentId, 1, "outsider", It.IsAny<DateTime>(), It.IsAny<string>(), UserId, It.IsAny<CancellationToken>()), Times.Never);
			_assignmentService.Verify(x => x.ExtendAsync(DepartmentId, 1, "in-group", new DateTime(2099, 1, 1), "Leave", UserId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_member_who_created_a_training_may_edit_and_delete_it()
		{
			ScopeIs(new TrainingManagementScope { UserId = UserId, DepartmentId = DepartmentId, ManagesOwn = true });
			_trainingService.Setup(x => x.GetTrainingByIdAsync(3)).ReturnsAsync(TrainingFor(3, UserId, "someone-else"));
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(3)).ReturnsAsync(TrainingFor(3, UserId, "someone-else"));
			_trainingService.Setup(x => x.DeleteTrainingAsync(DepartmentId, 3, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(TrainingDeleteOutcomes.Deleted);

			(await _controller.Edit(3)).Should().BeOfType<ViewResult>();

			await _controller.DeleteTraining(3);

			_trainingService.Verify(x => x.DeleteTrainingAsync(DepartmentId, 3, UserId, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Deleting_a_training_someone_completed_is_refused_and_the_officer_is_told_to_archive_it()
		{
			ScopeIs(new TrainingManagementScope { UserId = UserId, DepartmentId = DepartmentId, ManagesAll = true, ManagesOwn = true });
			_trainingService.Setup(x => x.GetTrainingRowByIdAsync(3)).ReturnsAsync(TrainingFor(3, UserId, "someone-else"));
			_trainingService.Setup(x => x.DeleteTrainingAsync(DepartmentId, 3, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(TrainingDeleteOutcomes.HasCompletions);
			_controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(_controller.HttpContext, Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

			var result = await _controller.DeleteTraining(3);

			result.Should().BeOfType<RedirectToActionResult>();
			_controller.TempData["TrainingError"].Should().Be("DeleteHasCompletionsError");
		}

		#endregion Rights

		#region Module switch, editor parsing and the report column

		[Test]
		public async Task Every_training_page_is_closed_when_the_department_switched_the_module_off()
		{
			_departmentSettings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings { TrainingDisabled = true });
			var context = new ActionExecutingContext(new ActionContext(_controller.HttpContext, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(),
				new Dictionary<string, object>(), _controller);
			var ran = false;

			await _controller.OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult<ActionExecutedContext>(null); });

			ran.Should().BeFalse();
			context.Result.Should().BeOfType<NotFoundResult>();
		}

		[Test]
		public async Task Training_pages_run_while_the_module_is_on()
		{
			_departmentSettings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentModuleSettings());
			var context = new ActionExecutingContext(new ActionContext(_controller.HttpContext, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(),
				new Dictionary<string, object>(), _controller);
			var ran = false;

			await _controller.OnActionExecutionAsync(context, () => { ran = true; return Task.FromResult<ActionExecutedContext>(null); });

			ran.Should().BeTrue();
		}

		[Test]
		public void The_editor_reads_question_types_and_every_correct_option_of_a_multiple_choice_question()
		{
			var questions = TrainingsController.ParseQuestions(Form(
				("question_1", "Which are PPE?"), ("questionType_1", "1"), ("questionId_1", "40"),
				("answerForQuestion_1_1", "Helmet"), ("answerForQuestion_1_2", "Gloves"), ("answerForQuestion_1_3", "Radio"),
				("answerId_1_1", "401"), ("correct_1", "1"), ("correct_1", "2"),
				("question_2", "The sky is blue"), ("questionType_2", "2"), ("answerForQuestion_2_1", "True"), ("answerForQuestion_2_2", "False"), ("correct_2", "1"),
				("question_x", "ignored"), ("questionType_3", "9")));

			questions.Should().HaveCount(2);
			questions[0].Id.Should().Be(40);
			questions[0].Type.Should().Be((int)TrainingQuestionTypes.MultiChoice);
			questions[0].Answers.Select(a => a.Correct).Should().Equal(true, true, false);
			questions[0].Answers[0].Id.Should().Be(401);
			questions[1].Type.Should().Be((int)TrainingQuestionTypes.TrueFalse);
			questions[1].Answers.Count(a => a.Correct).Should().Be(1);
		}

		[Test]
		public void The_editor_builds_audience_rules_and_everyone_stands_alone()
		{
			var rules = TrainingsController.BuildRules(new TrainingEditorModel { UserIds = new List<string> { "a", "A" }, GroupIds = new List<int> { 3, 3 }, ShiftIds = new List<int> { 9 } });
			rules.Select(r => (r.Kind, r.RefId)).Should().BeEquivalentTo(new[] { ((int)ContentAudienceKinds.User, "a"), ((int)ContentAudienceKinds.Group, "3"), ((int)ContentAudienceKinds.Shift, "9") });

			TrainingsController.BuildRules(new TrainingEditorModel { AssignEveryone = true, GroupIds = new List<int> { 3 } })
				.Should().ContainSingle().Which.Kind.Should().Be((int)ContentAudienceKinds.Everyone);
		}

		[Test]
		public void The_report_shows_complete_only_for_members_whose_state_is_complete_whatever_the_quiz()
		{
			// T-D12: a no-quiz training used to show Complete for every assignee, Not Viewed included.
			TrainingStatePresentation.IsComplete(new TrainingReportRow { State = TrainingAssignmentStates.NotStarted, Assignment = new TrainingUser() }).Should().BeFalse();
			TrainingStatePresentation.IsComplete(new TrainingReportRow { State = TrainingAssignmentStates.Failed, Assignment = new TrainingUser { Score = 40 } }).Should().BeFalse();
			TrainingStatePresentation.IsComplete(new TrainingReportRow { State = TrainingAssignmentStates.Exempt, Assignment = new TrainingUser() }).Should().BeFalse();
			TrainingStatePresentation.IsComplete(new TrainingReportRow { State = TrainingAssignmentStates.Complete, Assignment = new TrainingUser { Complete = true } }).Should().BeTrue();
		}

		[Test]
		public void Local_dates_from_date_inputs_are_read_strictly()
		{
			TrainingsController.ParseLocalDate("2026-12-01").Should().Be(new DateTime(2026, 12, 1));
			TrainingsController.ParseLocalDate("12/01/2026").Should().BeNull();
			TrainingsController.ParseLocalDate(null).Should().BeNull();
		}

		[Test]
		public async Task A_new_training_never_takes_its_stamps_or_creator_from_the_post()
		{
			ScopeIs(new TrainingManagementScope { UserId = UserId, DepartmentId = DepartmentId, ManagesAll = true, ManagesOwn = true });
			Training saved = null;
			_trainingService.Setup(x => x.SaveAsync(It.IsAny<Training>(), It.IsAny<CancellationToken>())).Callback<Training, CancellationToken>((t, _) => saved = t).ReturnsAsync((Training t, CancellationToken _) => t);
			_assignmentService.Setup(x => x.SaveAudienceAsync(It.IsAny<Training>(), It.IsAny<IEnumerable<IContentAudienceRule>>(), UserId, It.IsAny<TrainingRightScope>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new TrainingAssignmentOutcome());

			var model = new NewTrainingModel { TrainingId = 77, Name = "Pump ops", MinimumScore = 80, UserIds = new List<string> { "member-2" } };
			await _controller.New(model, Form(("Notified", "2020-01-01"), ("CreatedByUserId", "someone-else")), new List<IFormFile>());

			saved.Should().NotBeNull();
			saved.TrainingId.Should().Be(0);
			saved.CreatedByUserId.Should().Be(UserId);
			saved.Notified.Should().BeNull();
			saved.ArchivedOn.Should().BeNull();
		}

		[Test]
		public async Task A_pass_mark_outside_0_to_100_is_refused_on_the_server()
		{
			ScopeIs(new TrainingManagementScope { UserId = UserId, DepartmentId = DepartmentId, ManagesAll = true, ManagesOwn = true });

			var result = await _controller.New(new NewTrainingModel { Name = "Pump ops", MinimumScore = 150, OpenToAllMembers = true }, Form(), new List<IFormFile>());

			result.Should().BeOfType<ViewResult>();
			_controller.ModelState.ContainsKey(nameof(TrainingEditorModel.MinimumScore)).Should().BeTrue();
			_trainingService.Verify(x => x.SaveAsync(It.IsAny<Training>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		#endregion Module switch, editor parsing and the report column
	}
}
