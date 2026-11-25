using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Training;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// A training is opened by its assignees and by whoever manages it. Being a member of the department used to be
	/// enough to read any training, its quiz and its attachments by id, and to list all of them.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class TrainingsControllerAccessTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "member";

		private Mock<ITrainingService> _trainingService;
		private Mock<IAuthorizationService> _authorizationService;
		private TrainingsController _controller;

		[SetUp]
		public void SetUp()
		{
			_trainingService = new Mock<ITrainingService>();
			_authorizationService = new Mock<IAuthorizationService>();

			// A member with no training rights: they open what is assigned to them and manage nothing.
			_authorizationService.Setup(x => x.GetTrainingManagementScopeAsync(UserId, DepartmentId))
				.ReturnsAsync(TrainingManagementScope.None(UserId, DepartmentId));

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

			_controller = new TrainingsController(
				Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IDepartmentsService>(),
				_trainingService.Object,
				Mock.Of<IPersonnelRolesService>(),
				_authorizationService.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private static Training TrainingFor(int id, string createdBy, params string[] assignees) => new Training
		{
			TrainingId = id,
			DepartmentId = DepartmentId,
			Name = "Training " + id,
			CreatedByUserId = createdBy,
			Users = assignees.Select(x => new TrainingUser { UserId = x }).ToList(),
			Questions = new List<TrainingQuestion>(),
			Attachments = new List<TrainingAttachment>()
		};

		[Test]
		public async Task Index_lists_only_the_trainings_the_member_may_open()
		{
			_trainingService.Setup(x => x.GetAllTrainingsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Training>
			{
				TrainingFor(1, "officer", UserId),
				TrainingFor(2, "officer", "someone-else")
			});

			var result = await _controller.Index();

			var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<TrainingIndexModel>().Subject;
			model.Trainings.Select(x => x.TrainingId).Should().Equal(1);
		}

		[Test]
		public async Task Index_also_lists_a_training_that_is_open_to_all_members()
		{
			var open = TrainingFor(4, "officer", "someone-else");
			open.OpenToAllMembers = true;
			_trainingService.Setup(x => x.GetAllTrainingsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Training>
			{
				TrainingFor(2, "officer", "someone-else"),
				open
			});

			var result = await _controller.Index();

			var model = result.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<TrainingIndexModel>().Subject;
			model.Trainings.Select(x => x.TrainingId).Should().Equal(4);
		}

		[Test]
		public async Task The_quiz_of_an_open_training_is_refused_to_a_member_it_was_not_assigned_to()
		{
			var open = TrainingFor(4, "officer", "someone-else");
			open.OpenToAllMembers = true;
			_trainingService.Setup(x => x.GetTrainingByIdAsync(4)).ReturnsAsync(open);

			(await _controller.Quiz(4, CancellationToken.None)).Should().NotBeOfType<ViewResult>();
		}

		[Test]
		public async Task An_attachment_of_an_open_training_is_served_to_any_member()
		{
			var open = TrainingFor(4, "officer", "someone-else");
			open.OpenToAllMembers = true;
			_trainingService.Setup(x => x.GetTrainingAttachmentByIdAsync(8)).ReturnsAsync(new TrainingAttachment
			{
				TrainingAttachmentId = 8,
				TrainingId = 4,
				FileName = "sog.pdf",
				FileType = "application/pdf",
				Data = new byte[] { 1, 2, 3 }
			});
			_trainingService.Setup(x => x.GetTrainingByIdAsync(4)).ReturnsAsync(open);

			(await _controller.GetTrainingAttachment(8)).Should().NotBeNull();
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
		public async Task Quiz_refuses_a_training_that_is_not_assigned_to_the_member()
		{
			_trainingService.Setup(x => x.GetTrainingByIdAsync(2)).ReturnsAsync(TrainingFor(2, "officer", "someone-else"));

			var result = await _controller.Quiz(2, CancellationToken.None);

			result.Should().NotBeOfType<ViewResult>();
			_trainingService.Verify(x => x.SetTrainingAsViewedAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Quiz_opens_for_an_assignee()
		{
			_trainingService.Setup(x => x.GetTrainingByIdAsync(1)).ReturnsAsync(TrainingFor(1, "officer", UserId));

			var result = await _controller.Quiz(1, CancellationToken.None);

			result.Should().BeOfType<ViewResult>();
		}

		[Test]
		public async Task A_quiz_answer_from_someone_it_was_not_assigned_to_is_not_recorded()
		{
			_trainingService.Setup(x => x.GetTrainingByIdAsync(2)).ReturnsAsync(TrainingFor(2, "officer", "someone-else"));

			await _controller.Quiz(new ViewTrainingModel { Training = new Training { TrainingId = 2 } },
				new FormCollection(new Dictionary<string, StringValues>()));

			_trainingService.Verify(x => x.RecordTrainingQuizResultAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task An_attachment_of_a_training_that_is_not_assigned_to_the_member_is_not_served()
		{
			_trainingService.Setup(x => x.GetTrainingAttachmentByIdAsync(7)).ReturnsAsync(new TrainingAttachment
			{
				TrainingAttachmentId = 7,
				TrainingId = 2,
				FileName = "exam.pdf",
				FileType = "application/pdf",
				Data = new byte[] { 1, 2, 3 }
			});
			_trainingService.Setup(x => x.GetTrainingByIdAsync(2)).ReturnsAsync(TrainingFor(2, "officer", "someone-else"));

			(await _controller.GetTrainingAttachment(7)).Should().BeNull();
		}

		[Test]
		public async Task The_edit_page_with_its_answer_key_is_refused_to_a_member_who_does_not_manage_the_training()
		{
			// Assigned to the member, so they may open it - but not edit it.
			_trainingService.Setup(x => x.GetTrainingByIdAsync(1)).ReturnsAsync(TrainingFor(1, "officer", UserId));

			(await _controller.Edit(1)).Should().NotBeOfType<ViewResult>();
			(await _controller.Report(1)).Should().NotBeOfType<ViewResult>();

			await _controller.DeleteTraining(1);
			await _controller.ResetUserTraining(1, "someone-else");

			_trainingService.Verify(x => x.DeleteTrainingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_trainingService.Verify(x => x.ResetUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_member_who_created_a_training_may_edit_and_delete_it()
		{
			_authorizationService.Setup(x => x.GetTrainingManagementScopeAsync(UserId, DepartmentId))
				.ReturnsAsync(new TrainingManagementScope { UserId = UserId, DepartmentId = DepartmentId, ManagesOwn = true });
			_trainingService.Setup(x => x.GetTrainingByIdAsync(3)).ReturnsAsync(TrainingFor(3, UserId, "someone-else"));

			(await _controller.Edit(3)).Should().BeOfType<ViewResult>();

			await _controller.DeleteTraining(3);

			_trainingService.Verify(x => x.DeleteTrainingAsync(3, It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
