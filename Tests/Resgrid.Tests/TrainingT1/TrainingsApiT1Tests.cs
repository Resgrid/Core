using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Models.v4.Trainings;
using Resgrid.Web.ServicesCore.Helpers;
using ApiTrainingsController = Resgrid.Web.Services.Controllers.v4.TrainingsController;

namespace Resgrid.Tests.TrainingT1
{
	/// <summary>
	/// v4 Trainings, Training T1 (Omni): the transcript, categories and outside training submissions are refused while the Trainings
	/// module is off; a member reads only their own transcript unless they review the other member's records; a submission maps
	/// its fields onto a Pending draft and refuses an oversized or unknown evidence file before the service sees it.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class TrainingsApiT1Tests
	{
		private const int DepartmentId = 10;
		private const string UserId = "member-user";
		private const string OtherMember = "other-member";

		private Mock<IAuthorizationService> _authorization;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<ILearningRecordService> _records;
		private DepartmentModuleSettings _modules;
		private TrainingManagementScope _scope;
		private ApiTrainingsController _controller;
		private Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_authorization = new Mock<IAuthorizationService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_records = new Mock<ILearningRecordService>();
			_modules = new DepartmentModuleSettings();
			_scope = TrainingManagementScope.None(UserId, DepartmentId);

			_settings.Setup(x => x.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _modules);
			_authorization.Setup(x => x.GetTrainingManagementScopeAsync(UserId, DepartmentId)).ReturnsAsync(() => _scope);
			_records.Setup(x => x.GetCategoriesAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<TrainingCategory>
			{
				new TrainingCategory { TrainingCategoryId = 3, DepartmentId = DepartmentId, Name = "Fire suppression", Code = "FS", SortOrder = 1 },
				new TrainingCategory { TrainingCategoryId = 4, DepartmentId = DepartmentId, Name = "Removed", IsDeleted = true }
			});
			_records.Setup(x => x.GetTranscriptAsync(DepartmentId, It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<IEnumerable<int>>(),
				It.IsAny<IEnumerable<int>>(), false)).ReturnsAsync((int d, string user, DateTime? f, DateTime? t, IEnumerable<int> c, IEnumerable<int> s, bool v) => new List<LearningRecord>
				{
					Record(1, user, LearningRecordStatuses.Verified, 3),
					Record(2, user, LearningRecordStatuses.Pending, null),
					Record(3, user, LearningRecordStatuses.Voided, 3)
				});

			var httpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
			_activity = new Activity("TrainingsApiT1Tests").Start();

			_controller = new ApiTrainingsController(new Mock<ITrainingService>().Object, new Mock<ITrainingAssignmentService>().Object, new Mock<IMyTrainingService>().Object,
				_authorization.Object, _settings.Object, _records.Object)
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

		private static LearningRecord Record(int id, string userId, LearningRecordStatuses status, int? categoryId) => new LearningRecord
		{
			LearningRecordId = id,
			DepartmentId = DepartmentId,
			UserId = userId,
			Title = "Live fire " + id,
			CourseCode = "LF-" + id,
			CompletedOn = new DateTime(2026, 10, id, 12, 0, 0, DateTimeKind.Utc),
			Hours = 2.5m,
			CategoryId = categoryId,
			SourceKind = (int)LearningRecordSourceKinds.ExternalSubmission,
			Status = (int)status,
			CeHours = 2m,
			CeProvider = "State Fire Academy",
			CeCourseNumber = "SFA-100",
			IsBackfill = id == 2,
			PossibleDuplicateOfId = id == 2 ? 1 : (int?)null
		};

		private static SubmitExternalTrainingInput ValidInput() => new SubmitExternalTrainingInput
		{
			Title = "  Hazmat Awareness  ",
			CompletedOn = DateTime.UtcNow.Date.AddDays(-3),
			Hours = 4.256m,
			CategoryId = 3,
			CeProvider = " State Fire Academy ",
			CeCourseNumber = "HM-101",
			CeHours = 4m,
			Method = (int)MethodsOfTraining.Online,
			Notes = "Online module"
		};

		[Test]
		public async Task Module_off_refuses_the_transcript_categories_and_submissions()
		{
			_modules.TrainingDisabled = true;

			(await _controller.GetTranscript()).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.GetTrainingCategories()).Result.Should().BeOfType<NotFoundResult>();
			(await _controller.SubmitExternalTraining(ValidInput(), CancellationToken.None)).Result.Should().BeOfType<NotFoundResult>();

			_records.Verify(x => x.SubmitExternalAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<LearningRecordDraft>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Own_transcript_lists_live_records_with_category_names()
		{
			var response = await _controller.GetTranscript();

			var result = response.Value;
			result.Should().NotBeNull();
			result.Data.Select(r => r.Id).Should().Equal(2, 1);
			var verified = result.Data.Single(r => r.Id == 1);
			verified.CategoryName.Should().Be("Fire suppression");
			verified.StatusName.Should().Be("Verified");
			verified.SourceKindName.Should().Be("ExternalSubmission");
			verified.CeProvider.Should().Be("State Fire Academy");
			verified.CeCourseNumber.Should().Be("SFA-100");
			verified.CourseCode.Should().Be("LF-1");
			var pending = result.Data.Single(r => r.Id == 2);
			pending.IsBackfill.Should().BeTrue();
			pending.PossibleDuplicate.Should().BeTrue();
			pending.CategoryName.Should().BeNull();
			_records.Verify(x => x.GetTranscriptAsync(DepartmentId, UserId, null, null, null, null, false), Times.Once);
		}

		[Test]
		public async Task A_member_cannot_read_another_members_transcript_without_the_right()
		{
			var response = await _controller.GetTranscript(OtherMember);

			response.Result.Should().BeOfType<UnauthorizedResult>();
			_records.Verify(x => x.GetTranscriptAsync(It.IsAny<int>(), OtherMember, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<IEnumerable<int>>(),
				It.IsAny<IEnumerable<int>>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task A_group_admin_reads_only_their_groups_members()
		{
			_scope = new TrainingManagementScope { UserId = UserId, DepartmentId = DepartmentId, AdministeredMemberUserIds = new HashSet<string> { OtherMember } };

			(await _controller.GetTranscript(OtherMember)).Value.Data.Should().OnlyContain(r => r.Id == 1 || r.Id == 2);
			(await _controller.GetTranscript("someone-else")).Result.Should().BeOfType<UnauthorizedResult>();
		}

		[Test]
		public async Task A_department_wide_reviewer_reads_another_members_transcript_in_the_window()
		{
			_scope = new TrainingManagementScope { UserId = UserId, DepartmentId = DepartmentId, ManagesAll = true };
			var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			var to = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

			var response = await _controller.GetTranscript(OtherMember, to, from);

			response.Value.Data.Should().HaveCount(2);
			_records.Verify(x => x.GetTranscriptAsync(DepartmentId, OtherMember, from, to, null, null, false), Times.Once);
		}

		[Test]
		public async Task Categories_leave_out_removed_ones()
		{
			var response = await _controller.GetTrainingCategories();

			response.Value.Data.Should().ContainSingle();
			response.Value.Data[0].CategoryId.Should().Be(3);
			response.Value.Data[0].Code.Should().Be("FS");
		}

		[Test]
		public async Task Submission_maps_its_fields_onto_a_pending_draft_with_the_evidence_file()
		{
			LearningRecordDraft draft = null;
			string fileName = null, contentType = null;
			byte[] data = null;
			_records.Setup(x => x.SubmitExternalAsync(DepartmentId, UserId, It.IsAny<LearningRecordDraft>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.Callback((int d, string u, LearningRecordDraft dr, string n, string t, byte[] b, CancellationToken c) => { draft = dr; fileName = n; contentType = t; data = b; })
				.ReturnsAsync(() => new LearningRecord
				{
					LearningRecordId = 77, DepartmentId = DepartmentId, UserId = UserId, Title = draft.Title, CompletedOn = draft.CompletedOn, Hours = draft.Hours,
					CategoryId = draft.CategoryId, SourceKind = (int)LearningRecordSourceKinds.ExternalSubmission, Status = (int)LearningRecordStatuses.Pending
				});
			var input = ValidInput();
			input.FileName = "certs/card.PDF";
			input.ContentType = "text/html";
			input.Data = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });

			var response = await _controller.SubmitExternalTraining(input, CancellationToken.None);

			response.Value.Should().NotBeNull();
			response.Value.Error.Should().BeNull();
			response.Value.Data.Id.Should().Be(77);
			response.Value.Data.StatusName.Should().Be("Pending");
			response.Value.Data.CategoryName.Should().Be("Fire suppression");
			draft.UserId.Should().Be(UserId);
			draft.Title.Should().Be("Hazmat Awareness");
			draft.CompletedOn.Should().Be(input.CompletedOn);
			draft.CompletedOn.Kind.Should().Be(DateTimeKind.Utc);
			draft.Hours.Should().Be(4.26m);
			draft.CategoryId.Should().Be(3);
			draft.MethodOfTraining.Should().Be((int)MethodsOfTraining.Online);
			draft.CeProvider.Should().Be("State Fire Academy");
			draft.CeCourseNumber.Should().Be("HM-101");
			draft.CeHours.Should().Be(4m);
			draft.Notes.Should().Be("Online module");
			draft.Status.Should().Be(LearningRecordStatuses.Pending);
			fileName.Should().Be("card.PDF");
			contentType.Should().Be("application/pdf", "the stored type follows the extension, not the client's claim");
			data.Should().Equal(1, 2, 3, 4);
		}

		[Test]
		public async Task Submissions_switched_off_answer_a_failure_with_the_reason()
		{
			_records.Setup(x => x.SubmitExternalAsync(DepartmentId, UserId, It.IsAny<LearningRecordDraft>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((LearningRecord)null);

			var response = await _controller.SubmitExternalTraining(ValidInput(), CancellationToken.None);

			var refused = response.Result.Should().BeOfType<BadRequestObjectResult>().Subject.Value.Should().BeOfType<ExternalTrainingSubmissionResult>().Subject;
			refused.Error.Should().Be("submissions_disabled");
			refused.Data.Should().BeNull();
		}

		[Test]
		public async Task A_service_refusal_code_comes_back_as_the_error()
		{
			_records.Setup(x => x.SubmitExternalAsync(DepartmentId, UserId, It.IsAny<LearningRecordDraft>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("learning_record_category_invalid"));

			var response = await _controller.SubmitExternalTraining(ValidInput(), CancellationToken.None);

			response.Result.Should().BeOfType<BadRequestObjectResult>().Subject.Value.Should().BeOfType<ExternalTrainingSubmissionResult>()
				.Which.Error.Should().Be("learning_record_category_invalid");
		}

		[TestCase("exe", "file_type_not_allowed")]
		[TestCase("html", "file_type_not_allowed")]
		[TestCase("", "file_type_not_allowed")]
		public async Task Unknown_evidence_types_are_refused_before_the_service(string extension, string error)
		{
			var input = ValidInput();
			input.FileName = extension.Length == 0 ? "evidence" : "evidence." + extension;
			input.ContentType = "application/pdf";
			input.Data = Convert.ToBase64String(new byte[] { 1, 2, 3 });

			await AssertRefused(input, error);
		}

		[Test]
		public async Task Oversized_evidence_is_refused_before_the_service()
		{
			var input = ValidInput();
			input.FileName = "card.png";
			input.Data = Convert.ToBase64String(new byte[LearningRecordFile.MaxBytes + 1]);
			await AssertRefused(input, "file_too_large");

			input.Data = new string('A', 20 * 1024 * 1024);
			await AssertRefused(input, "file_too_large");
		}

		[Test]
		public async Task Evidence_of_exactly_the_limit_is_accepted()
		{
			_records.Setup(x => x.SubmitExternalAsync(DepartmentId, UserId, It.IsAny<LearningRecordDraft>(), "card.png", "image/png", It.Is<byte[]>(b => b.Length == LearningRecordFile.MaxBytes),
				It.IsAny<CancellationToken>())).ReturnsAsync(Record(9, UserId, LearningRecordStatuses.Pending, null));
			var input = ValidInput();
			input.FileName = "card.png";
			input.Data = Convert.ToBase64String(new byte[LearningRecordFile.MaxBytes]);

			var response = await _controller.SubmitExternalTraining(input, CancellationToken.None);

			response.Value.Data.Id.Should().Be(9);
		}

		[Test]
		public async Task Malformed_or_nameless_evidence_is_refused()
		{
			var input = ValidInput();
			input.FileName = "card.pdf";
			input.Data = "not base64!";
			await AssertRefused(input, "file_invalid");

			input.FileName = "  ";
			input.Data = Convert.ToBase64String(new byte[] { 1 });
			await AssertRefused(input, "file_name_required");
		}

		[Test]
		public async Task Invalid_fields_are_refused_with_their_code()
		{
			var input = ValidInput(); input.Title = " ";
			await AssertRefused(input, "title_required");
			input = ValidInput(); input.Title = new string('x', LearningRecord.MaxTitleLength + 1);
			await AssertRefused(input, "title_too_long");
			input = ValidInput(); input.CompletedOn = DateTime.UtcNow.AddDays(3);
			await AssertRefused(input, "date_invalid");
			input = ValidInput(); input.CompletedOn = default;
			await AssertRefused(input, "date_invalid");
			input = ValidInput(); input.Hours = 0;
			await AssertRefused(input, "hours_invalid");
			input = ValidInput(); input.Hours = null;
			await AssertRefused(input, "hours_invalid");
			input = ValidInput(); input.CeHours = -1;
			await AssertRefused(input, "ce_hours_invalid");
			input = ValidInput(); input.CategoryId = 4;
			await AssertRefused(input, "category_invalid");
			input = ValidInput(); input.CategoryId = 99;
			await AssertRefused(input, "category_invalid");
			input = ValidInput(); input.Method = 42;
			await AssertRefused(input, "method_invalid");
			input = ValidInput(); input.Notes = new string('n', LearningRecord.MaxNotesLength + 1);
			await AssertRefused(input, "text_too_long");
		}

		private async Task AssertRefused(SubmitExternalTrainingInput input, string error)
		{
			var response = await _controller.SubmitExternalTraining(input, CancellationToken.None);

			response.Result.Should().BeOfType<BadRequestObjectResult>().Subject.Value.Should().BeOfType<ExternalTrainingSubmissionResult>()
				.Which.Error.Should().Be(error);
			_records.Verify(x => x.SubmitExternalAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<LearningRecordDraft>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
