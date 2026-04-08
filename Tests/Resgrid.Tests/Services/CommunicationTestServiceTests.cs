using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace CommunicationTestServiceTests
	{
		public class with_the_communication_test_service : TestBase
		{
			protected Mock<ICommunicationTestRepository> _communicationTestRepoMock;
			protected Mock<ICommunicationTestRunRepository> _communicationTestRunRepoMock;
			protected Mock<ICommunicationTestResultRepository> _communicationTestResultRepoMock;
			protected Mock<IDepartmentsService> _departmentsServiceMock;
			protected Mock<IUserProfileService> _userProfileServiceMock;

			protected ICommunicationTestService _communicationTestService;

			protected override void Before_all_tests()
			{
				base.Before_all_tests();

				_communicationTestRepoMock = new Mock<ICommunicationTestRepository>();
				_communicationTestRunRepoMock = new Mock<ICommunicationTestRunRepository>();
				_communicationTestResultRepoMock = new Mock<ICommunicationTestResultRepository>();
				_departmentsServiceMock = new Mock<IDepartmentsService>();
				_userProfileServiceMock = new Mock<IUserProfileService>();

				_communicationTestService = new CommunicationTestService(
					_communicationTestRepoMock.Object,
					_communicationTestRunRepoMock.Object,
					_communicationTestResultRepoMock.Object,
					_departmentsServiceMock.Object,
					_userProfileServiceMock.Object
				);
			}
		}

		[TestFixture]
		public class when_starting_a_test_run : with_the_communication_test_service
		{
			[Test]
			public async Task should_create_results_per_user_per_channel()
			{
				var testId = Guid.NewGuid();
				var test = new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestSms = true,
					TestEmail = true,
					TestVoice = false,
					TestPush = true,
					ResponseWindowMinutes = 60,
					Active = true
				};

				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(test);

				var members = new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 },
					new DepartmentMember { UserId = TestData.Users.TestUser2Id, DepartmentId = 1 }
				};
				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(members);

				var profiles = new Dictionary<string, UserProfile>
				{
					{ TestData.Users.TestUser1Id, new UserProfile { UserId = TestData.Users.TestUser1Id, MembershipEmail = "user1@test.com", MobileNumber = "5551234567", MobileCarrier = (int)MobileCarriers.Att, EmailVerified = true, MobileNumberVerified = true } },
					{ TestData.Users.TestUser2Id, new UserProfile { UserId = TestData.Users.TestUser2Id, MembershipEmail = "user2@test.com", MobileNumber = "5559876543", MobileCarrier = (int)MobileCarriers.Verizon, EmailVerified = null, MobileNumberVerified = false } }
				};
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(profiles);

				_communicationTestRunRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) =>
					{
						r.CommunicationTestRunId = Guid.NewGuid();
						return r;
					});

				_communicationTestResultRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestResult r, CancellationToken c, bool f) => r);

				var run = await _communicationTestService.StartTestRunAsync(testId, 1, TestData.Users.TestUser1Id);

				run.Should().NotBeNull();
				run.TotalUsersTested.Should().Be(2);
				run.RunCode.Should().StartWith("CT-");

				// 3 channels (SMS, Email, Push) x 2 users = 6 results
				_communicationTestResultRepoMock.Verify(
					x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true),
					Times.Exactly(6));
			}

			[Test]
			public async Task should_block_send_for_pending_verification()
			{
				var testId = Guid.NewGuid();
				var test = new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestSms = true,
					TestEmail = false,
					TestVoice = false,
					TestPush = false,
					ResponseWindowMinutes = 60,
					Active = true
				};

				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(test);

				var members = new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 }
				};
				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(members);

				var profiles = new Dictionary<string, UserProfile>
				{
					{ TestData.Users.TestUser1Id, new UserProfile { UserId = TestData.Users.TestUser1Id, MobileNumber = "5551234567", MobileCarrier = (int)MobileCarriers.Att, MobileNumberVerified = false } }
				};
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(profiles);

				_communicationTestRunRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) =>
					{
						r.CommunicationTestRunId = Guid.NewGuid();
						return r;
					});

				CommunicationTestResult savedResult = null;
				_communicationTestResultRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true))
					.Callback<CommunicationTestResult, CancellationToken, bool>((r, c, f) => savedResult = r)
					.ReturnsAsync((CommunicationTestResult r, CancellationToken c, bool f) => r);

				await _communicationTestService.StartTestRunAsync(testId, 1, TestData.Users.TestUser1Id);

				savedResult.Should().NotBeNull();
				savedResult.SendAttempted.Should().BeFalse();
				savedResult.VerificationStatus.Should().Be((int)ContactVerificationStatus.Pending);
			}

			[Test]
			public async Task should_allow_send_for_grandfathered_verification()
			{
				var testId = Guid.NewGuid();
				var test = new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					TestSms = false,
					TestVoice = false,
					TestPush = false,
					ResponseWindowMinutes = 60,
					Active = true
				};

				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(test);

				var members = new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 }
				};
				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(members);

				var profiles = new Dictionary<string, UserProfile>
				{
					{ TestData.Users.TestUser1Id, new UserProfile { UserId = TestData.Users.TestUser1Id, MembershipEmail = "user1@test.com", EmailVerified = null } }
				};
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(profiles);

				_communicationTestRunRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) =>
					{
						r.CommunicationTestRunId = Guid.NewGuid();
						return r;
					});

				CommunicationTestResult savedResult = null;
				_communicationTestResultRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true))
					.Callback<CommunicationTestResult, CancellationToken, bool>((r, c, f) => savedResult = r)
					.ReturnsAsync((CommunicationTestResult r, CancellationToken c, bool f) => r);

				await _communicationTestService.StartTestRunAsync(testId, 1, TestData.Users.TestUser1Id);

				savedResult.Should().NotBeNull();
				savedResult.SendAttempted.Should().BeTrue();
				savedResult.SendSucceeded.Should().BeTrue();
				savedResult.VerificationStatus.Should().Be((int)ContactVerificationStatus.Grandfathered);
			}
		}

		[TestFixture]
		public class when_recording_responses : with_the_communication_test_service
		{
			[Test]
			public async Task should_record_sms_response_by_run_code()
			{
				var runId = Guid.NewGuid();
				var run = new CommunicationTestRun
				{
					CommunicationTestRunId = runId,
					Status = (int)CommunicationTestRunStatus.AwaitingResponses,
					RunCode = "CT-A7X3"
				};

				_communicationTestRunRepoMock.Setup(x => x.GetRunByRunCodeAsync("CT-A7X3")).ReturnsAsync(run);

				var results = new List<CommunicationTestResult>
				{
					new CommunicationTestResult
					{
						CommunicationTestResultId = Guid.NewGuid(),
						CommunicationTestRunId = runId,
						UserId = TestData.Users.TestUser1Id,
						Channel = (int)CommunicationTestChannel.Sms,
						ContactValue = "5551234567",
						SendAttempted = true,
						SendSucceeded = true,
						Responded = false
					}
				};

				_communicationTestResultRepoMock.Setup(x => x.GetResultsByRunIdAsync(runId)).ReturnsAsync(results);
				_communicationTestResultRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestResult r, CancellationToken c, bool f) => r);
				_communicationTestRunRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) => r);

				var success = await _communicationTestService.RecordSmsResponseAsync("CT-A7X3", "5551234567");

				success.Should().BeTrue();
				results[0].Responded.Should().BeTrue();
				results[0].RespondedOn.Should().NotBeNull();
			}

			[Test]
			public async Task should_record_email_response_by_token()
			{
				var token = Guid.NewGuid().ToString("N");
				var result = new CommunicationTestResult
				{
					CommunicationTestResultId = Guid.NewGuid(),
					CommunicationTestRunId = Guid.NewGuid(),
					Channel = (int)CommunicationTestChannel.Email,
					SendAttempted = true,
					SendSucceeded = true,
					Responded = false,
					ResponseToken = token
				};

				_communicationTestResultRepoMock.Setup(x => x.GetResultByResponseTokenAsync(token)).ReturnsAsync(result);
				_communicationTestResultRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestResult r, CancellationToken c, bool f) => r);

				var run = new CommunicationTestRun { CommunicationTestRunId = result.CommunicationTestRunId };
				_communicationTestRunRepoMock.Setup(x => x.GetByIdAsync(result.CommunicationTestRunId)).ReturnsAsync(run);
				_communicationTestResultRepoMock.Setup(x => x.GetResultsByRunIdAsync(result.CommunicationTestRunId)).ReturnsAsync(new List<CommunicationTestResult> { result });
				_communicationTestRunRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) => r);

				var success = await _communicationTestService.RecordEmailResponseAsync(token);

				success.Should().BeTrue();
				result.Responded.Should().BeTrue();
			}

			[Test]
			public async Task should_record_push_response_by_token()
			{
				var token = Guid.NewGuid().ToString("N");
				var result = new CommunicationTestResult
				{
					CommunicationTestResultId = Guid.NewGuid(),
					CommunicationTestRunId = Guid.NewGuid(),
					Channel = (int)CommunicationTestChannel.Push,
					SendAttempted = true,
					SendSucceeded = true,
					Responded = false,
					ResponseToken = token
				};

				_communicationTestResultRepoMock.Setup(x => x.GetResultByResponseTokenAsync(token)).ReturnsAsync(result);
				_communicationTestResultRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestResult r, CancellationToken c, bool f) => r);

				var run = new CommunicationTestRun { CommunicationTestRunId = result.CommunicationTestRunId };
				_communicationTestRunRepoMock.Setup(x => x.GetByIdAsync(result.CommunicationTestRunId)).ReturnsAsync(run);
				_communicationTestResultRepoMock.Setup(x => x.GetResultsByRunIdAsync(result.CommunicationTestRunId)).ReturnsAsync(new List<CommunicationTestResult> { result });
				_communicationTestRunRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) => r);

				var success = await _communicationTestService.RecordPushResponseAsync(token);

				success.Should().BeTrue();
				result.Responded.Should().BeTrue();
			}

			[Test]
			public async Task should_not_record_response_for_completed_run()
			{
				var run = new CommunicationTestRun
				{
					CommunicationTestRunId = Guid.NewGuid(),
					Status = (int)CommunicationTestRunStatus.Completed,
					RunCode = "CT-DONE"
				};

				_communicationTestRunRepoMock.Setup(x => x.GetRunByRunCodeAsync("CT-DONE")).ReturnsAsync(run);

				var success = await _communicationTestService.RecordSmsResponseAsync("CT-DONE", "5551234567");

				success.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_completing_expired_runs : with_the_communication_test_service
		{
			[Test]
			public async Task should_complete_expired_runs()
			{
				var testId = Guid.NewGuid();
				var run = new CommunicationTestRun
				{
					CommunicationTestRunId = Guid.NewGuid(),
					CommunicationTestId = testId,
					Status = (int)CommunicationTestRunStatus.AwaitingResponses,
					StartedOn = DateTime.UtcNow.AddMinutes(-120)
				};

				var test = new CommunicationTest
				{
					CommunicationTestId = testId,
					ResponseWindowMinutes = 60
				};

				_communicationTestRunRepoMock.Setup(x => x.GetOpenRunsAsync()).ReturnsAsync(new List<CommunicationTestRun> { run });
				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(test);
				_communicationTestRunRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) => r);

				await _communicationTestService.CompleteExpiredRunsAsync();

				run.Status.Should().Be((int)CommunicationTestRunStatus.Completed);
				run.CompletedOn.Should().NotBeNull();
			}
		}

		[TestFixture]
		public class when_processing_scheduled_tests : with_the_communication_test_service
		{
			[Test]
			public async Task should_process_weekly_test_on_matching_day()
			{
				var today = DateTime.UtcNow.DayOfWeek;
				var test = new CommunicationTest
				{
					CommunicationTestId = Guid.NewGuid(),
					DepartmentId = 1,
					ScheduleType = (int)CommunicationTestScheduleType.Weekly,
					Sunday = today == DayOfWeek.Sunday,
					Monday = today == DayOfWeek.Monday,
					Tuesday = today == DayOfWeek.Tuesday,
					Wednesday = today == DayOfWeek.Wednesday,
					Thursday = today == DayOfWeek.Thursday,
					Friday = today == DayOfWeek.Friday,
					Saturday = today == DayOfWeek.Saturday,
					TestSms = true,
					Active = true,
					ResponseWindowMinutes = 60,
					CreatedByUserId = TestData.Users.TestUser1Id
				};

				_communicationTestRepoMock.Setup(x => x.GetActiveTestsForScheduleTypeAsync((int)CommunicationTestScheduleType.Weekly))
					.ReturnsAsync(new List<CommunicationTest> { test });
				_communicationTestRepoMock.Setup(x => x.GetActiveTestsForScheduleTypeAsync((int)CommunicationTestScheduleType.Monthly))
					.ReturnsAsync(new List<CommunicationTest>());
				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(test.CommunicationTestId)).ReturnsAsync(test);

				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentMember>());
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>());

				_communicationTestRunRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) =>
					{
						r.CommunicationTestRunId = Guid.NewGuid();
						return r;
					});

				await _communicationTestService.ProcessScheduledTestsAsync();

				_communicationTestRunRepoMock.Verify(
					x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true),
					Times.AtLeastOnce);
			}
		}
	}
}
