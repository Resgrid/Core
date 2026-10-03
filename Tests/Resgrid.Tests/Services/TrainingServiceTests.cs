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
		private Mock<IDepartmentSettingsService> _departmentSettingsServiceMock;
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
			_departmentSettingsServiceMock = new Mock<IDepartmentSettingsService>();

			_trainingService = new TrainingService(
				_trainingRepository,
				_attachmentRepository,
				_userRepository,
				_questionRepository,
				_communicationServiceMock.Object,
				_departmentServiceMock.Object,
				_departmentSettingsServiceMock.Object
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

		[Test]
		public async Task SaveAsync_Should_Keep_Underlined_And_Escaped_Text_In_Training_Content()
		{
			// Arrange - what Quill posts for an underlined acronym and an escaped address (#181)
			var training = new Training
			{
				Name = "Training",
				Description = "<p>The <u>NORA</u> program</p>",
				TrainingText = "<p>Contact NORA &lt;nora@example.org&gt; today</p>",
				DepartmentId = 1,
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			};

			// Act
			var result = await _trainingService.SaveAsync(training);

			// Assert
			result.Description.Should().Be("<p>The <u>NORA</u> program</p>");
			result.TrainingText.Should().Be("<p>Contact NORA &lt;nora@example.org&gt; today</p>");
		}

		#endregion

		#region SaveAsync Question Editing Tests (#180)

		private void SeedTrainingWithTwoQuestions()
		{
			_trainingRepository.SeedTraining(new Training
			{
				TrainingId = 1,
				DepartmentId = 1,
				Name = "Quiz Training",
				Description = "Description",
				TrainingText = "Text",
				CreatedByUserId = TestData.Users.TestUser1Id,
				CreatedOn = DateTime.UtcNow
			});

			_questionRepository.SeedQuestion(new TrainingQuestion
			{
				TrainingQuestionId = 1,
				TrainingId = 1,
				Question = "Q1",
				Answers = new List<TrainingQuestionAnswer>
				{
					new TrainingQuestionAnswer { TrainingQuestionAnswerId = 10, TrainingQuestionId = 1, Answer = "A", Correct = true },
					new TrainingQuestionAnswer { TrainingQuestionAnswerId = 11, TrainingQuestionId = 1, Answer = "B" }
				}
			});

			_questionRepository.SeedQuestion(new TrainingQuestion
			{
				TrainingQuestionId = 2,
				TrainingId = 1,
				Question = "Q2",
				Answers = new List<TrainingQuestionAnswer>
				{
					new TrainingQuestionAnswer { TrainingQuestionAnswerId = 20, TrainingQuestionId = 2, Answer = "C", Correct = true },
					new TrainingQuestionAnswer { TrainingQuestionAnswerId = 21, TrainingQuestionId = 2, Answer = "D" }
				}
			});
		}

		private static TrainingQuestion PostedQuestion(int questionId, string text, params (int id, string answer)[] answers)
		{
			return new TrainingQuestion
			{
				TrainingQuestionId = questionId,
				TrainingId = 1,
				Question = text,
				Answers = answers.Select(a => new TrainingQuestionAnswer { TrainingQuestionAnswerId = a.id, TrainingQuestionId = questionId, Answer = a.answer }).ToList()
			};
		}

		private async Task<Training> EditTrainingAsync(params TrainingQuestion[] postedQuestions)
		{
			var existing = await _trainingService.GetTrainingByIdAsync(1);
			existing.Questions = postedQuestions.ToList();

			return await _trainingService.SaveAsync(existing);
		}

		[Test]
		public async Task SaveAsync_Editing_Unchanged_Questions_Should_Not_Duplicate_Them()
		{
			SeedTrainingWithTwoQuestions();

			// Save the same questions twice, as two consecutive edits would
			await EditTrainingAsync(PostedQuestion(1, "Q1", (10, "A"), (11, "B")), PostedQuestion(2, "Q2", (20, "C"), (21, "D")));
			await EditTrainingAsync(PostedQuestion(1, "Q1", (10, "A"), (11, "B")), PostedQuestion(2, "Q2", (20, "C"), (21, "D")));

			var stored = (await _questionRepository.GetTrainingQuestionsByTrainingIdAsync(1)).ToList();
			stored.Should().HaveCount(2);
			stored.Select(x => x.TrainingQuestionId).Should().BeEquivalentTo(new[] { 1, 2 });
		}

		[Test]
		public async Task SaveAsync_Editing_Should_Update_Questions_In_Place_And_Delete_Removed_Ones()
		{
			SeedTrainingWithTwoQuestions();

			// Q1 reworded, Q2 removed, a new question added
			await EditTrainingAsync(PostedQuestion(1, "Q1 reworded", (10, "A"), (11, "B")), PostedQuestion(0, "Q3", (0, "E")));

			var stored = (await _questionRepository.GetTrainingQuestionsByTrainingIdAsync(1)).ToList();
			stored.Should().HaveCount(2);
			stored.Should().Contain(x => x.TrainingQuestionId == 1 && x.Question == "Q1 reworded");
			stored.Should().Contain(x => x.Question == "Q3" && x.TrainingQuestionId > 2);
			stored.Should().NotContain(x => x.TrainingQuestionId == 2);
		}

		[Test]
		public async Task SaveAsync_Editing_Should_Not_Touch_Questions_Of_Another_Training()
		{
			SeedTrainingWithTwoQuestions();
			_questionRepository.SeedQuestion(new TrainingQuestion { TrainingQuestionId = 99, TrainingId = 2, Question = "Other training" });

			// A posted id that is not one of this training's questions is added as a new question
			await EditTrainingAsync(PostedQuestion(1, "Q1", (10, "A")), PostedQuestion(99, "Hijacked", (0, "X")));

			(await _questionRepository.GetByIdAsync(99)).Question.Should().Be("Other training");
			(await _questionRepository.GetByIdAsync(99)).TrainingId.Should().Be(2);

			var stored = (await _questionRepository.GetTrainingQuestionsByTrainingIdAsync(1)).ToList();
			stored.Should().HaveCount(2);
			stored.Should().Contain(x => x.Question == "Hijacked" && x.TrainingQuestionId != 99);
		}

		[Test]
		public async Task SaveAsync_Editing_Question_With_All_Answers_Replaced_Should_Recreate_It()
		{
			SeedTrainingWithTwoQuestions();

			// None of Q2's stored answers survive, so the old row (and its answers) has to go
			await EditTrainingAsync(PostedQuestion(1, "Q1", (10, "A"), (11, "B")), PostedQuestion(2, "Q2", (0, "New answer")));

			var stored = (await _questionRepository.GetTrainingQuestionsByTrainingIdAsync(1)).ToList();
			stored.Should().HaveCount(2);
			stored.Should().NotContain(x => x.TrainingQuestionId == 2);

			var recreated = stored.Single(x => x.Question == "Q2");
			recreated.TrainingQuestionId.Should().BeGreaterThan(2);
			recreated.Answers.Should().ContainSingle(x => x.Answer == "New answer");
		}

		[Test]
		public async Task SaveAsync_Editing_Without_Questions_Loaded_Should_Leave_Stored_Questions()
		{
			SeedTrainingWithTwoQuestions();

			var existing = await _trainingRepository.GetByIdAsync(1);
			existing.Questions = null;
			await _trainingService.SaveAsync(existing);

			(await _questionRepository.GetTrainingQuestionsByTrainingIdAsync(1)).Should().HaveCount(2);
		}

		#endregion

		#region SendInitialTrainingNoticeAsync Tests

		[Test]
		public async Task SendInitialTrainingNoticeAsync_Should_Notify_Each_User_From_The_Department_Number()
		{
			var previousDoNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;

			try
			{
				_departmentSettingsServiceMock.Setup(x => x.GetTextToCallNumberForDepartmentAsync(1)).ReturnsAsync("15555550100");

				var training = new Training
				{
					TrainingId = 1,
					DepartmentId = 1,
					Name = "Hose Lays",
					Users = new List<TrainingUser>
					{
						new TrainingUser { UserId = TestData.Users.TestUser1Id },
						new TrainingUser { UserId = TestData.Users.TestUser2Id }
					}
				};

				var result = await _trainingService.SendInitialTrainingNoticeAsync(training);

				result.Should().BeTrue();
				_communicationServiceMock.Verify(x => x.SendNotificationAsync(It.IsAny<string>(), 1, "New Training (Hose Lays) assigned to you", "15555550100",
					It.IsAny<Department>(), "New Training Notice", It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Exactly(2));
				_communicationServiceMock.Verify(x => x.SendNotificationAsync(TestData.Users.TestUser2Id, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()), Times.Once);
			}
			finally
			{
				Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = previousDoNotBroadcast;
			}
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