using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Mocks;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class TrainingServiceTests
	{
		private MockTrainingRepository _trainingRepository;
		private MockTrainingAttachmentRepository _attachmentRepository;
		private MockTrainingQuestionRepository _questionRepository;
		private MockTrainingUserRepository _userRepository;
		private Mock<ICommunicationService> _communicationServiceMock;
		private Mock<IDepartmentsService> _departmentServiceMock;
		private TrainingService _trainingService;

		[SetUp]
		public void SetUp()
		{
			_trainingRepository = new MockTrainingRepository();
			_attachmentRepository = new MockTrainingAttachmentRepository();
			_questionRepository = new MockTrainingQuestionRepository();
			_userRepository = new MockTrainingUserRepository();
			_communicationServiceMock = new Mock<ICommunicationService>();
			_departmentServiceMock = new Mock<IDepartmentsService>();

			_trainingService = new TrainingService(
				_trainingRepository,
				_attachmentRepository,
				_userRepository,
				_questionRepository,
				_communicationServiceMock.Object,
				_departmentServiceMock.Object
			);
		}

		#region GetTrainingByIdAsync Tests

		[Test]
		public async Task GetTrainingByIdAsync_Should_Return_Training_With_Questions_And_Attachments()
		{
			// Arrange
			var training = new Training
			{
				TrainingId = 1,
				DepartmentId = 1,
				Name = "Fire Safety Training",
				Description = "Basic fire safety",
				TrainingText = "Learn fire safety basics",
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			};
			_trainingRepository.SeedTraining(training);

			var question = new TrainingQuestion
			{
				TrainingQuestionId = 1,
				TrainingId = 1,
				Question = "What is the correct response to a fire?"
			};
			_questionRepository.SeedQuestion(question);

			var attachment = new TrainingAttachment
			{
				TrainingAttachmentId = 1,
				TrainingId = 1,
				FileName = "fire_safety.pdf"
			};
			_attachmentRepository.SeedAttachment(attachment);

			// Act
			var result = await _trainingService.GetTrainingByIdAsync(1);

			// Assert
			result.Should().NotBeNull();
			result.TrainingId.Should().Be(1);
			result.Name.Should().Be("Fire Safety Training");
			result.Questions.Should().NotBeNull();
			result.Questions.Should().HaveCount(1);
			result.Attachments.Should().NotBeNull();
			result.Attachments.Should().HaveCount(1);
		}

		[Test]
		public async Task GetTrainingByIdAsync_Should_Return_Null_For_NonExistent_Training()
		{
			// Act
			var result = await _trainingService.GetTrainingByIdAsync(999);

			// Assert
			result.Should().BeNull();
		}

		#endregion

		#region GetAllTrainingsForDepartmentAsync Tests

		[Test]
		public async Task GetAllTrainingsForDepartmentAsync_Should_Return_Trainings_For_Department()
		{
			// Arrange
			_trainingRepository.SeedTraining(new Training
			{
				TrainingId = 1,
				DepartmentId = 1,
				Name = "Training 1",
				Description = "Description 1",
				TrainingText = "Text 1",
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			});
			_trainingRepository.SeedTraining(new Training
			{
				TrainingId = 2,
				DepartmentId = 1,
				Name = "Training 2",
				Description = "Description 2",
				TrainingText = "Text 2",
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			});
			_trainingRepository.SeedTraining(new Training
			{
				TrainingId = 3,
				DepartmentId = 2,
				Name = "Training 3 (Other Dept)",
				Description = "Description 3",
				TrainingText = "Text 3",
				CreatedByUserId = TestData.Users.TestUser5Id,
				CreatedOn = DateTime.UtcNow
			});

			// Act
			var result = await _trainingService.GetAllTrainingsForDepartmentAsync(1);

			// Assert
			result.Should().NotBeNull();
			result.Should().HaveCount(2);
			result.All(t => t.DepartmentId == 1).Should().BeTrue();
		}

		[Test]
		public async Task GetAllTrainingsForDepartmentAsync_Should_Return_Empty_List_For_NonExistent_Department()
		{
			// Act
			var result = await _trainingService.GetAllTrainingsForDepartmentAsync(999);

			// Assert
			result.Should().NotBeNull();
			result.Should().BeEmpty();
		}

		#endregion

		#region SaveAsync Tests

		[Test]
		public async Task SaveAsync_Should_Create_New_Training()
		{
			// Arrange
			var training = new Training
			{
				Name = "New Training",
				Description = "New Description",
				TrainingText = "New Text",
				DepartmentId = 1,
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			};

			// Act
			var result = await _trainingService.SaveAsync(training);

			// Assert
			result.Should().NotBeNull();
			result.TrainingId.Should().BeGreaterThan(0);
			result.Name.Should().Be("New Training");
		}

		[Test]
		public async Task SaveAsync_Should_Update_Existing_Training()
		{
			// Arrange
			var existing = new Training
			{
				TrainingId = 1,
				DepartmentId = 1,
				Name = "Original Name",
				Description = "Original Description",
				TrainingText = "Original Text",
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			};
			_trainingRepository.SeedTraining(existing);

			// Act
			existing.Name = "Updated Name";
			existing.Description = "Updated Description";
			var result = await _trainingService.SaveAsync(existing);

			// Assert
			result.Should().NotBeNull();
			result.TrainingId.Should().Be(1);
			result.Name.Should().Be("Updated Name");
			result.Description.Should().Be("Updated Description");
		}

		[Test]
		public async Task SaveAsync_Should_Save_Training_With_Questions()
		{
			// Arrange
			var training = new Training
			{
				Name = "Quiz Training",
				Description = "Training with questions",
				TrainingText = "Text",
				DepartmentId = 1,
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow,
				Questions = new List<TrainingQuestion>
				{
					new TrainingQuestion
					{
						Question = "Question 1?",
						Answers = new List<TrainingQuestionAnswer>
						{
							new TrainingQuestionAnswer { Answer = "Answer A", Correct = true },
							new TrainingQuestionAnswer { Answer = "Answer B", Correct = false }
						}
					}
				}
			};

			// Act
			var result = await _trainingService.SaveAsync(training);

			// Assert
			result.Should().NotBeNull();
			result.Questions.Should().NotBeNull();
			result.Questions.Should().HaveCount(1);
		}

		[Test]
		public async Task SaveAsync_Should_Sanitize_Html_In_Description_And_Text()
		{
			// Arrange
			var training = new Training
			{
				Name = "Training",
				Description = "<script>alert('xss')</script><p>Safe content</p>",
				TrainingText = "<script>alert('xss')</script><p>Safe training text</p>",
				DepartmentId = 1,
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			};

			// Act
			var result = await _trainingService.SaveAsync(training);

			// Assert
			result.Description.Should().NotContain("<script>");
			result.TrainingText.Should().NotContain("<script>");
		}

		#endregion

		#region DeleteTrainingAsync Tests

		[Test]
		public async Task DeleteTrainingAsync_Should_Delete_Training()
		{
			// Arrange
			var training = new Training
			{
				TrainingId = 1,
				DepartmentId = 1,
				Name = "Training to Delete",
				Description = "Description",
				TrainingText = "Text",
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			};
			_trainingRepository.SeedTraining(training);

			// Act
			var result = await _trainingService.DeleteTrainingAsync(1);

			// Assert
			result.Should().BeTrue();
			var deleted = await _trainingRepository.GetByIdAsync(1);
			deleted.Should().BeNull();
		}

		#endregion

		#region SetTrainingAsViewedAsync Tests

		[Test]
		public async Task SetTrainingAsViewedAsync_Should_Mark_Training_As_Viewed()
		{
			// Arrange
			_trainingRepository.SeedTraining(new Training
			{
				TrainingId = 1,
				DepartmentId = 1,
				Name = "Training",
				Description = "Description",
				TrainingText = "Text",
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			});

			_userRepository.SeedUser(new TrainingUser
			{
				TrainingUserId = 1,
				TrainingId = 1,
				UserId = TestData.Users.TestUser1Id,
				Viewed = false,
				Complete = false
			});

			// Act
			var result = await _trainingService.SetTrainingAsViewedAsync(1, TestData.Users.TestUser1Id);

			// Assert
			result.Should().NotBeNull();
			result.Viewed.Should().BeTrue();
			result.ViewedOn.Should().NotBeNull();
		}

		[Test]
		public async Task SetTrainingAsViewedAsync_Should_Auto_Complete_When_No_Questions()
		{
			// Arrange
			_trainingRepository.SeedTraining(new Training
			{
				TrainingId = 1,
				DepartmentId = 1,
				Name = "Training",
				Description = "Description",
				TrainingText = "Text",
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			});

			_userRepository.SeedUser(new TrainingUser
			{
				TrainingUserId = 1,
				TrainingId = 1,
				UserId = TestData.Users.TestUser1Id,
				Viewed = false,
				Complete = false
			});

			// Act
			var result = await _trainingService.SetTrainingAsViewedAsync(1, TestData.Users.TestUser1Id);

			// Assert
			result.Should().NotBeNull();
			result.Complete.Should().BeTrue();
			result.CompletedOn.Should().NotBeNull();
		}

		[Test]
		public async Task SetTrainingAsViewedAsync_Should_Return_Null_For_NonExistent_User()
		{
			// Act
			var result = await _trainingService.SetTrainingAsViewedAsync(999, TestData.Users.TestUser1Id);

			// Assert
			result.Should().BeNull();
		}

		#endregion

		#region RecordTrainingQuizResultAsync Tests

		[Test]
		public async Task RecordTrainingQuizResultAsync_Should_Record_Score_And_Mark_Complete()
		{
			// Arrange
			_trainingRepository.SeedTraining(new Training
			{
				TrainingId = 1,
				DepartmentId = 1,
				Name = "Quiz Training",
				Description = "Description",
				TrainingText = "Text",
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow,
				Questions = new List<TrainingQuestion>
				{
					new TrainingQuestion { TrainingQuestionId = 1, TrainingId = 1, Question = "Q1" },
					new TrainingQuestion { TrainingQuestionId = 2, TrainingId = 1, Question = "Q2" },
					new TrainingQuestion { TrainingQuestionId = 3, TrainingId = 1, Question = "Q3" }
				}
			});

			foreach (var q in _trainingRepository.Trainings.First().Questions)
			{
				_questionRepository.SeedQuestion(q);
			}

			_userRepository.SeedUser(new TrainingUser
			{
				TrainingUserId = 1,
				TrainingId = 1,
				UserId = TestData.Users.TestUser1Id,
				Viewed = true,
				Complete = false
			});

			// Act - User gets 2 out of 3 correct (66.67%)
			var result = await _trainingService.RecordTrainingQuizResultAsync(1, TestData.Users.TestUser1Id, 2);

			// Assert
			result.Should().NotBeNull();
			result.Complete.Should().BeTrue();
			result.CompletedOn.Should().NotBeNull();
			result.Score.Should().BeApproximately(66.67, 0.1);
		}

		#endregion

		#region ResetUserAsync Tests

		[Test]
		public async Task ResetUserAsync_Should_Reset_User_Progress()
		{
			// Arrange
			_userRepository.SeedUser(new TrainingUser
			{
				TrainingUserId = 1,
				TrainingId = 1,
				UserId = TestData.Users.TestUser1Id,
				Viewed = true,
				ViewedOn = DateTime.UtcNow.AddDays(-1),
				Complete = true,
				CompletedOn = DateTime.UtcNow.AddDays(-1),
				Score = 85.5
			});

			// Act
			var result = await _trainingService.ResetUserAsync(1, TestData.Users.TestUser1Id);

			// Assert
			result.Should().NotBeNull();
			result.Viewed.Should().BeFalse();
			result.ViewedOn.Should().BeNull();
			result.Complete.Should().BeFalse();
			result.CompletedOn.Should().BeNull();
			result.Score.Should().Be(0);
		}

		#endregion

		#region GetTrainingUsersForUserAsync Tests

		[Test]
		public async Task GetTrainingUsersForUserAsync_Should_Return_User_Trainings()
		{
			// Arrange
			_userRepository.SeedUser(new TrainingUser
			{
				TrainingUserId = 1,
				TrainingId = 1,
				UserId = TestData.Users.TestUser1Id,
				Viewed = true
			});
			_userRepository.SeedUser(new TrainingUser
			{
				TrainingUserId = 2,
				TrainingId = 2,
				UserId = TestData.Users.TestUser1Id,
				Viewed = false
			});
			_userRepository.SeedUser(new TrainingUser
			{
				TrainingUserId = 3,
				TrainingId = 1,
				UserId = TestData.Users.TestUser2Id,
				Viewed = true
			});

			// Act
			var result = await _trainingService.GetTrainingUsersForUserAsync(TestData.Users.TestUser1Id);

			// Assert
			result.Should().NotBeNull();
			result.Should().HaveCount(2);
			result.All(u => u.UserId == TestData.Users.TestUser1Id).Should().BeTrue();
		}

		#endregion
	}
}