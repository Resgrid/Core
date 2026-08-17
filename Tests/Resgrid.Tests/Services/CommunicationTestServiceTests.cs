using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
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
			protected Mock<ICommunicationTestTargetRepository> _communicationTestTargetRepoMock;
			protected Mock<IDepartmentsService> _departmentsServiceMock;
			protected Mock<IUserProfileService> _userProfileServiceMock;
			protected Mock<IDepartmentGroupsService> _departmentGroupsServiceMock;
			protected Mock<IPersonnelRolesService> _personnelRolesServiceMock;
			protected Mock<IDepartmentSettingsService> _departmentSettingsServiceMock;
			protected Mock<ISmsService> _smsServiceMock;
			protected Mock<IEmailService> _emailServiceMock;
			protected Mock<IPushService> _pushServiceMock;
			protected Mock<IOutboundVoiceProvider> _outboundVoiceProviderMock;
			protected Mock<IPhoneNumberProcesserProvider> _phoneNumberProcesserMock;
			protected Mock<IQueueService> _queueServiceMock;
			protected Mock<IUnitOfWork> _unitOfWorkMock;

			protected Dictionary<Guid, CommunicationTestRun> _savedRuns;
			protected List<CommunicationTestResult> _savedResults;

			protected ICommunicationTestService _communicationTestService;

			/// <summary>
			/// Makes the run and result repositories behave like a store instead of a stub, which the
			/// two-phase (start → queue → build → deliver) flow needs: BuildRunResultsAsync re-reads the
			/// run the start call saved, and its "already built" guard reads back saved results.
			/// </summary>
			protected void SetupRunAndResultPersistence()
			{
				_savedRuns = new Dictionary<Guid, CommunicationTestRun>();
				_savedResults = new List<CommunicationTestResult>();

				_communicationTestRunRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) =>
					{
						if (r.CommunicationTestRunId == Guid.Empty)
							r.CommunicationTestRunId = Guid.NewGuid();

						_savedRuns[r.CommunicationTestRunId] = r;
						return r;
					});

				_communicationTestRunRepoMock
					.Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
					.ReturnsAsync((Guid id) => _savedRuns.TryGetValue(id, out var run) ? run : null);

				_communicationTestResultRepoMock
					.Setup(x => x.GetResultsByRunIdAsync(It.IsAny<Guid>()))
					.ReturnsAsync((Guid id) => _savedResults.Where(r => r.CommunicationTestRunId == id).ToList());

				_communicationTestResultRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true))
					.Callback<CommunicationTestResult, CancellationToken, bool>((r, c, f) =>
					{
						if (!_savedResults.Contains(r))
							_savedResults.Add(r);
					})
					.ReturnsAsync((CommunicationTestResult r, CancellationToken c, bool f) => r);
			}

			/// <summary>
			/// Starts a run the way a caller does, then runs the worker-side build step, and returns the
			/// built run. Delivery is deliberately left out so audience tests stay off the send path.
			/// </summary>
			protected async Task<CommunicationTestRun> StartAndBuildAsync(Guid testId, int departmentId, string userId)
			{
				var run = await _communicationTestService.StartTestRunAsync(testId, departmentId, userId);
				if (run == null)
					return null;

				return await _communicationTestService.BuildRunResultsAsync(run.CommunicationTestRunId);
			}

			protected override void Before_all_tests()
			{
				base.Before_all_tests();

				_communicationTestRepoMock = new Mock<ICommunicationTestRepository>();
				_communicationTestRunRepoMock = new Mock<ICommunicationTestRunRepository>();
				_communicationTestResultRepoMock = new Mock<ICommunicationTestResultRepository>();
				_communicationTestTargetRepoMock = new Mock<ICommunicationTestTargetRepository>();
				_departmentsServiceMock = new Mock<IDepartmentsService>();
				_userProfileServiceMock = new Mock<IUserProfileService>();
				_departmentGroupsServiceMock = new Mock<IDepartmentGroupsService>();
				_personnelRolesServiceMock = new Mock<IPersonnelRolesService>();
				_departmentSettingsServiceMock = new Mock<IDepartmentSettingsService>();
				_smsServiceMock = new Mock<ISmsService>();
				_emailServiceMock = new Mock<IEmailService>();
				_pushServiceMock = new Mock<IPushService>();
				_outboundVoiceProviderMock = new Mock<IOutboundVoiceProvider>();
				_phoneNumberProcesserMock = new Mock<IPhoneNumberProcesserProvider>();
				_queueServiceMock = new Mock<IQueueService>();
				_unitOfWorkMock = new Mock<IUnitOfWork>();

				_savedRuns = new Dictionary<Guid, CommunicationTestRun>();
				_savedResults = new List<CommunicationTestResult>();

				_communicationTestTargetRepoMock.Setup(x => x.GetTargetsByTestIdAsync(It.IsAny<Guid>()))
					.ReturnsAsync(new List<CommunicationTestTarget>());

				_queueServiceMock.Setup(x => x.EnqueueCommunicationTestAsync(It.IsAny<CommunicationTestQueueItem>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(true);

				_communicationTestService = new CommunicationTestService(
					_communicationTestRepoMock.Object,
					_communicationTestRunRepoMock.Object,
					_communicationTestResultRepoMock.Object,
					_communicationTestTargetRepoMock.Object,
					_departmentsServiceMock.Object,
					_userProfileServiceMock.Object,
					_departmentGroupsServiceMock.Object,
					_personnelRolesServiceMock.Object,
					_departmentSettingsServiceMock.Object,
					_smsServiceMock.Object,
					_emailServiceMock.Object,
					_pushServiceMock.Object,
					_outboundVoiceProviderMock.Object,
					_phoneNumberProcesserMock.Object,
					_queueServiceMock.Object,
					_unitOfWorkMock.Object
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

				SetupRunAndResultPersistence();

				var run = await StartAndBuildAsync(testId, 1, TestData.Users.TestUser1Id);

				run.Should().NotBeNull();
				run.TotalUsersTested.Should().Be(2);
				run.RunCode.Should().StartWith("CT-");

				// 3 channels (SMS, Email, Push) x 2 users = 6 results
				_communicationTestResultRepoMock.Verify(
					x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true),
					Times.Exactly(6));
			}

			[Test]
			public async Task should_hand_the_run_to_the_worker_instead_of_building_or_sending_inline()
			{
				var testId = Guid.NewGuid();
				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					TestSms = true,
					ResponseWindowMinutes = 60,
					Active = true
				});

				SetupRunAndResultPersistence();

				CommunicationTestQueueItem queued = null;
				_queueServiceMock
					.Setup(x => x.EnqueueCommunicationTestAsync(It.IsAny<CommunicationTestQueueItem>(), It.IsAny<CancellationToken>()))
					.Callback<CommunicationTestQueueItem, CancellationToken>((i, c) => queued = i)
					.ReturnsAsync(true);

				var run = await _communicationTestService.StartTestRunAsync(testId, 1, TestData.Users.TestUser1Id);

				run.Should().NotBeNull();
				run.Status.Should().Be((int)CommunicationTestRunStatus.Pending);

				queued.Should().NotBeNull();
				queued.CommunicationTestRunId.Should().Be(run.CommunicationTestRunId.ToString());
				queued.CommunicationTestId.Should().Be(testId.ToString());
				queued.DepartmentId.Should().Be(1);

				// Nothing touched the department or the providers on the caller's thread.
				_departmentsServiceMock.Verify(x => x.GetAllMembersForDepartmentAsync(It.IsAny<int>()), Times.Never);
				_communicationTestResultRepoMock.Verify(
					x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true),
					Times.Never);
			}

			[Test]
			public async Task should_leave_the_run_pending_when_the_broker_publish_throws()
			{
				var testId = Guid.NewGuid();
				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					ResponseWindowMinutes = 60,
					Active = true
				});

				SetupRunAndResultPersistence();

				_queueServiceMock.Setup(x => x.EnqueueCommunicationTestAsync(It.IsAny<CommunicationTestQueueItem>(), It.IsAny<CancellationToken>()))
					.ThrowsAsync(new InvalidOperationException("broker down"));

				// The run must survive a broker outage so the recovery sweep can pick it up.
				var run = await _communicationTestService.StartTestRunAsync(testId, 1, TestData.Users.TestUser1Id);

				run.Should().NotBeNull();
				run.Status.Should().Be((int)CommunicationTestRunStatus.Pending);
			}

			[Test]
			public async Task should_not_rebuild_results_for_a_run_that_already_has_them()
			{
				var testId = Guid.NewGuid();
				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					ResponseWindowMinutes = 60,
					Active = true
				});

				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 }
				});
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>());

				SetupRunAndResultPersistence();

				var run = await _communicationTestService.StartTestRunAsync(testId, 1, TestData.Users.TestUser1Id);

				// At-least-once delivery: the same queue item arriving twice must not double the audience.
				await _communicationTestService.BuildRunResultsAsync(run.CommunicationTestRunId);
				await _communicationTestService.BuildRunResultsAsync(run.CommunicationTestRunId);

				_savedResults.Count.Should().Be(1);
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

				SetupRunAndResultPersistence();

				await StartAndBuildAsync(testId, 1, TestData.Users.TestUser1Id);

				var savedResult = _savedResults.SingleOrDefault();
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
					{ TestData.Users.TestUser1Id, new UserProfile { UserId = TestData.Users.TestUser1Id, MembershipEmail = "user1@test.com", EmailVerified = null, SendMessageEmail = true } }
				};
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(profiles);

				SetupRunAndResultPersistence();

				await StartAndBuildAsync(testId, 1, TestData.Users.TestUser1Id);

				var savedResult = _savedResults.SingleOrDefault();
				savedResult.Should().NotBeNull();
				savedResult.SendAttempted.Should().BeTrue();
				savedResult.VerificationStatus.Should().Be((int)ContactVerificationStatus.Grandfathered);
			}

			[Test]
			public async Task should_not_attempt_send_when_the_channel_is_switched_off_on_the_profile()
			{
				var testId = Guid.NewGuid();
				var test = new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					ResponseWindowMinutes = 60,
					Active = true
				};

				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(test);
				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 }
				});

				// Verified address, but every email opt-in is off: the department cannot actually
				// reach this person by email, so the test must not claim it tried.
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>
				{
					{ TestData.Users.TestUser1Id, new UserProfile { UserId = TestData.Users.TestUser1Id, MembershipEmail = "user1@test.com", EmailVerified = true } }
				});

				SetupRunAndResultPersistence();

				await StartAndBuildAsync(testId, 1, TestData.Users.TestUser1Id);

				var savedResult = _savedResults.SingleOrDefault();
				savedResult.Should().NotBeNull();
				savedResult.SendAttempted.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_saving_a_test_with_its_targets : with_the_communication_test_service
		{
			[Test]
			public async Task should_commit_the_test_and_its_targets_together()
			{
				var testId = Guid.NewGuid();
				var test = new CommunicationTest { CommunicationTestId = testId, DepartmentId = 1, Name = "Monthly Check" };

				_communicationTestRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTest>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTest t, CancellationToken c, bool f) => t);
				_communicationTestTargetRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestTarget>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestTarget t, CancellationToken c, bool f) => t);

				var saved = await _communicationTestService.SaveTestWithTargetsAsync(test, 1, new List<CommunicationTestTarget>
				{
					new CommunicationTestTarget { TargetType = (int)CommunicationTestTargetType.Group, TargetId = "7" }
				});

				saved.Should().NotBeNull();
				_unitOfWorkMock.Verify(x => x.CreateOrGetConnection(), Times.Once);
				_unitOfWorkMock.Verify(x => x.CommitChanges(), Times.Once);
				_unitOfWorkMock.Verify(x => x.DiscardChanges(), Times.Never);
			}

			[Test]
			public async Task should_roll_back_the_test_when_replacing_its_targets_fails()
			{
				var testId = Guid.NewGuid();
				var test = new CommunicationTest { CommunicationTestId = testId, DepartmentId = 1, Name = "Monthly Check" };

				_communicationTestRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTest>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTest t, CancellationToken c, bool f) => t);

				// Targets are cleared before the replacements are written. Without the rollback the test
				// would be left with no targets at all, which resolves to the whole department.
				_communicationTestTargetRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestTarget>(), It.IsAny<CancellationToken>(), true))
					.ThrowsAsync(new InvalidOperationException("insert failed"));

				var act = async () => await _communicationTestService.SaveTestWithTargetsAsync(test, 1, new List<CommunicationTestTarget>
				{
					new CommunicationTestTarget { TargetType = (int)CommunicationTestTargetType.Group, TargetId = "7" }
				});

				await act.Should().ThrowAsync<InvalidOperationException>();

				_unitOfWorkMock.Verify(x => x.DiscardChanges(), Times.Once);
				_unitOfWorkMock.Verify(x => x.CommitChanges(), Times.Never);
			}

			[Test]
			public async Task should_stamp_the_saved_test_id_onto_targets_built_before_it_had_one()
			{
				var assignedId = Guid.NewGuid();
				var test = new CommunicationTest { DepartmentId = 1, Name = "Monthly Check" };

				_communicationTestRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTest>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTest t, CancellationToken c, bool f) =>
					{
						t.CommunicationTestId = assignedId;
						return t;
					});

				var written = new List<CommunicationTestTarget>();
				_communicationTestTargetRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestTarget>(), It.IsAny<CancellationToken>(), true))
					.Callback<CommunicationTestTarget, CancellationToken, bool>((t, c, f) => written.Add(t))
					.ReturnsAsync((CommunicationTestTarget t, CancellationToken c, bool f) => t);

				// A new test has no id until it is saved, so the caller builds targets carrying Guid.Empty.
				await _communicationTestService.SaveTestWithTargetsAsync(test, 1, new List<CommunicationTestTarget>
				{
					new CommunicationTestTarget { CommunicationTestId = Guid.Empty, TargetType = (int)CommunicationTestTargetType.User, TargetId = TestData.Users.TestUser1Id }
				});

				written.Should().OnlyContain(t => t.CommunicationTestId == assignedId);
			}
		}

		[TestFixture]
		public class when_targeting_a_test : with_the_communication_test_service
		{
			[Test]
			public async Task should_only_test_members_matching_a_group_role_or_user_target()
			{
				var testId = Guid.NewGuid();
				var test = new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					ResponseWindowMinutes = 60,
					Active = true
				};

				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(test);

				_communicationTestTargetRepoMock.Setup(x => x.GetTargetsByTestIdAsync(testId)).ReturnsAsync(new List<CommunicationTestTarget>
				{
					new CommunicationTestTarget { CommunicationTestId = testId, DepartmentId = 1, TargetType = (int)CommunicationTestTargetType.Group, TargetId = "7" },
					new CommunicationTestTarget { CommunicationTestId = testId, DepartmentId = 1, TargetType = (int)CommunicationTestTargetType.Role, TargetId = "9" },
					new CommunicationTestTarget { CommunicationTestId = testId, DepartmentId = 1, TargetType = (int)CommunicationTestTargetType.User, TargetId = TestData.Users.TestUser3Id }
				});

				_departmentGroupsServiceMock.Setup(x => x.GetAllMembersForGroupAsync(7)).ReturnsAsync(new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { UserId = TestData.Users.TestUser1Id }
				});

				_personnelRolesServiceMock.Setup(x => x.GetAllMembersOfRoleAsync(9)).ReturnsAsync(new List<PersonnelRoleUser>
				{
					new PersonnelRoleUser { UserId = TestData.Users.TestUser2Id }
				});

				// TestUser4 is in the department but matches no target, so must not be tested.
				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 },
					new DepartmentMember { UserId = TestData.Users.TestUser2Id, DepartmentId = 1 },
					new DepartmentMember { UserId = TestData.Users.TestUser3Id, DepartmentId = 1 },
					new DepartmentMember { UserId = TestData.Users.TestUser4Id, DepartmentId = 1 }
				});

				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>());

				SetupRunAndResultPersistence();

				var run = await StartAndBuildAsync(testId, 1, TestData.Users.TestUser1Id);

				run.TotalUsersTested.Should().Be(3);
				_savedResults.Select(r => r.UserId).Should().BeEquivalentTo(new[]
				{
					TestData.Users.TestUser1Id,
					TestData.Users.TestUser2Id,
					TestData.Users.TestUser3Id
				});
			}

			[Test]
			public async Task should_test_the_whole_department_when_no_targets_are_set()
			{
				var testId = Guid.NewGuid();
				var test = new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					ResponseWindowMinutes = 60,
					Active = true
				};

				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(test);
				_communicationTestTargetRepoMock.Setup(x => x.GetTargetsByTestIdAsync(testId)).ReturnsAsync(new List<CommunicationTestTarget>());

				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 },
					new DepartmentMember { UserId = TestData.Users.TestUser2Id, DepartmentId = 1 }
				});
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>());

				SetupRunAndResultPersistence();

				var run = await StartAndBuildAsync(testId, 1, TestData.Users.TestUser1Id);

				run.TotalUsersTested.Should().Be(2);
			}

			[Test]
			public async Task should_ignore_a_target_for_someone_who_left_the_department()
			{
				var testId = Guid.NewGuid();
				var test = new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					ResponseWindowMinutes = 60,
					Active = true
				};

				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(test);
				_communicationTestTargetRepoMock.Setup(x => x.GetTargetsByTestIdAsync(testId)).ReturnsAsync(new List<CommunicationTestTarget>
				{
					new CommunicationTestTarget { CommunicationTestId = testId, DepartmentId = 1, TargetType = (int)CommunicationTestTargetType.User, TargetId = TestData.Users.TestUser1Id },
					new CommunicationTestTarget { CommunicationTestId = testId, DepartmentId = 1, TargetType = (int)CommunicationTestTargetType.User, TargetId = TestData.Users.TestUser2Id }
				});

				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 }
				});
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>());

				SetupRunAndResultPersistence();

				var run = await StartAndBuildAsync(testId, 1, TestData.Users.TestUser1Id);

				run.TotalUsersTested.Should().Be(1);
			}

			[Test]
			public async Task should_test_the_audience_the_run_started_with_when_targets_change_afterwards()
			{
				var testId = Guid.NewGuid();
				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					ResponseWindowMinutes = 60,
					Active = true
				});

				_communicationTestTargetRepoMock.Setup(x => x.GetTargetsByTestIdAsync(testId)).ReturnsAsync(new List<CommunicationTestTarget>
				{
					new CommunicationTestTarget { CommunicationTestId = testId, DepartmentId = 1, TargetType = (int)CommunicationTestTargetType.User, TargetId = TestData.Users.TestUser1Id }
				});

				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 },
					new DepartmentMember { UserId = TestData.Users.TestUser2Id, DepartmentId = 1 }
				});
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>());

				SetupRunAndResultPersistence();

				var run = await _communicationTestService.StartTestRunAsync(testId, 1, TestData.Users.TestUser1Id);

				// The test is re-targeted while the run is still sitting on the queue. The report has to
				// describe the audience the run was started for, so the worker must ignore the edit.
				_communicationTestTargetRepoMock.Setup(x => x.GetTargetsByTestIdAsync(testId)).ReturnsAsync(new List<CommunicationTestTarget>
				{
					new CommunicationTestTarget { CommunicationTestId = testId, DepartmentId = 1, TargetType = (int)CommunicationTestTargetType.User, TargetId = TestData.Users.TestUser2Id }
				});

				var built = await _communicationTestService.BuildRunResultsAsync(run.CommunicationTestRunId);

				built.TotalUsersTested.Should().Be(1);
				_savedResults.Select(r => r.UserId).Should().BeEquivalentTo(new[] { TestData.Users.TestUser1Id });
			}

			[Test]
			public async Task should_still_test_the_whole_department_when_targets_are_added_after_the_run_starts()
			{
				var testId = Guid.NewGuid();
				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					ResponseWindowMinutes = 60,
					Active = true
				});

				// Untargeted at start time, so the run covers everyone.
				_communicationTestTargetRepoMock.Setup(x => x.GetTargetsByTestIdAsync(testId)).ReturnsAsync(new List<CommunicationTestTarget>());

				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 },
					new DepartmentMember { UserId = TestData.Users.TestUser2Id, DepartmentId = 1 }
				});
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>());

				SetupRunAndResultPersistence();

				var run = await _communicationTestService.StartTestRunAsync(testId, 1, TestData.Users.TestUser1Id);

				// Narrowing the test after the fact must not narrow a run that was already started.
				_communicationTestTargetRepoMock.Setup(x => x.GetTargetsByTestIdAsync(testId)).ReturnsAsync(new List<CommunicationTestTarget>
				{
					new CommunicationTestTarget { CommunicationTestId = testId, DepartmentId = 1, TargetType = (int)CommunicationTestTargetType.User, TargetId = TestData.Users.TestUser1Id }
				});

				var built = await _communicationTestService.BuildRunResultsAsync(run.CommunicationTestRunId);

				built.TotalUsersTested.Should().Be(2);
			}

			[Test]
			public async Task should_fall_back_to_current_targeting_for_a_run_started_before_audience_snapshots()
			{
				var testId = Guid.NewGuid();
				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(new CommunicationTest
				{
					CommunicationTestId = testId,
					DepartmentId = 1,
					TestEmail = true,
					ResponseWindowMinutes = 60,
					Active = true
				});

				_communicationTestTargetRepoMock.Setup(x => x.GetTargetsByTestIdAsync(testId)).ReturnsAsync(new List<CommunicationTestTarget>
				{
					new CommunicationTestTarget { CommunicationTestId = testId, DepartmentId = 1, TargetType = (int)CommunicationTestTargetType.User, TargetId = TestData.Users.TestUser2Id }
				});

				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { UserId = TestData.Users.TestUser1Id, DepartmentId = 1 },
					new DepartmentMember { UserId = TestData.Users.TestUser2Id, DepartmentId = 1 }
				});
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>());

				SetupRunAndResultPersistence();

				// A Pending run as it looked before the snapshot column existed -- in flight across the
				// deploy that added it. It still has to test the targeted people, not the department.
				var run = new CommunicationTestRun
				{
					CommunicationTestRunId = Guid.NewGuid(),
					CommunicationTestId = testId,
					DepartmentId = 1,
					StartedOn = DateTime.UtcNow,
					Status = (int)CommunicationTestRunStatus.Pending,
					RunCode = "CT-ABCD",
					TargetedUserIds = null
				};
				_savedRuns[run.CommunicationTestRunId] = run;

				var built = await _communicationTestService.BuildRunResultsAsync(run.CommunicationTestRunId);

				built.TotalUsersTested.Should().Be(1);
				_savedResults.Select(r => r.UserId).Should().BeEquivalentTo(new[] { TestData.Users.TestUser2Id });
			}
		}

		[TestFixture]
		public class when_delivering_a_run : with_the_communication_test_service
		{
			private Guid _runId;
			private Guid _testId;

			private CommunicationTestResult SetupSingleResult(CommunicationTestChannel channel, string contactValue)
			{
				_runId = Guid.NewGuid();
				_testId = Guid.NewGuid();

				var run = new CommunicationTestRun
				{
					CommunicationTestRunId = _runId,
					CommunicationTestId = _testId,
					DepartmentId = 1,
					RunCode = "CT-A7X3",
					Status = (int)CommunicationTestRunStatus.Running
				};

				_communicationTestRunRepoMock.Setup(x => x.GetByIdAsync(_runId)).ReturnsAsync(run);
				_communicationTestRunRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) => r);

				_communicationTestRepoMock.Setup(x => x.GetByIdAsync(_testId)).ReturnsAsync(new CommunicationTest
				{
					CommunicationTestId = _testId,
					DepartmentId = 1,
					Name = "Monthly Check"
				});

				var result = new CommunicationTestResult
				{
					CommunicationTestResultId = Guid.NewGuid(),
					CommunicationTestRunId = _runId,
					DepartmentId = 1,
					UserId = TestData.Users.TestUser1Id,
					Channel = (int)channel,
					ContactValue = contactValue,
					SendAttempted = true,
					ResponseToken = Guid.NewGuid().ToString("N")
				};

				_communicationTestResultRepoMock.Setup(x => x.GetResultsByRunIdAsync(_runId))
					.ReturnsAsync(new List<CommunicationTestResult> { result });
				_communicationTestResultRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true))
					.ReturnsAsync((CommunicationTestResult r, CancellationToken c, bool f) => r);

				_departmentsServiceMock.Setup(x => x.GetDepartmentByIdAsync(1, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = 1, Name = "Test Dept" });
				_departmentSettingsServiceMock.Setup(x => x.GetTextToCallNumberForDepartmentAsync(1)).ReturnsAsync("15550001111");
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>
				{
					{ TestData.Users.TestUser1Id, new UserProfile { UserId = TestData.Users.TestUser1Id, FirstName = "Test", MembershipEmail = "user1@test.com", MobileNumber = "+15551234567" } }
				});

				return result;
			}

			[Test]
			public async Task should_send_the_run_code_over_sms_and_record_the_real_outcome()
			{
				var result = SetupSingleResult(CommunicationTestChannel.Sms, "5551234567");

				_phoneNumberProcesserMock.Setup(x => x.Process("+15551234567", null))
					.Returns(new PhoneNumberResult { IsValid = true, InternationalNumber = "+15551234567" });

				string sentBody = null;
				_smsServiceMock
					.Setup(x => x.SendCommunicationTestAsync("+15551234567", It.IsAny<string>(), "15550001111", It.IsAny<MobileCarriers>(), 1))
					.Callback<string, string, string, MobileCarriers, int>((n, m, d, c, i) => sentBody = m)
					.ReturnsAsync(true);

				var sent = await _communicationTestService.DeliverRunAsync(_runId);

				sent.Should().Be(1);
				sentBody.Should().Contain("CT-A7X3");
				result.SendSucceeded.Should().BeTrue();
				result.SentOn.Should().NotBeNull();
			}

			[Test]
			public async Task should_record_a_failed_send_rather_than_claiming_success()
			{
				var result = SetupSingleResult(CommunicationTestChannel.Email, "user1@test.com");

				_emailServiceMock
					.Setup(x => x.SendCommunicationTestEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
					.ReturnsAsync(false);

				var sent = await _communicationTestService.DeliverRunAsync(_runId);

				sent.Should().Be(0);
				result.SendSucceeded.Should().BeFalse();
				result.SentOn.Should().NotBeNull();
			}

			[Test]
			public async Task should_place_a_voice_call_carrying_the_response_token()
			{
				// ContactValue is the display form with the "+" stripped. The call has to normalise off
				// the raw profile number, or every international voice test fails to parse.
				var result = SetupSingleResult(CommunicationTestChannel.Voice, "15551234567");

				_phoneNumberProcesserMock.Setup(x => x.Process("+15551234567", null))
					.Returns(new PhoneNumberResult { IsValid = true, InternationalNumber = "+15551234567" });

				_outboundVoiceProviderMock
					.Setup(x => x.SendCommunicationTestCallAsync("+15551234567", result.ResponseToken))
					.ReturnsAsync(true);

				var sent = await _communicationTestService.DeliverRunAsync(_runId);

				sent.Should().Be(1);
				_outboundVoiceProviderMock.Verify(x => x.SendCommunicationTestCallAsync("+15551234567", result.ResponseToken), Times.Once);
			}

			[Test]
			public async Task should_place_a_voice_call_on_the_home_number_when_the_profile_routes_there()
			{
				var result = SetupSingleResult(CommunicationTestChannel.Voice, "441632960123");

				// Mobile calling off, home calling on: dispatch would ring the home number, so the test
				// has to ring it too -- and off the raw value, not the "+"-stripped display form.
				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>
				{
					{
						TestData.Users.TestUser1Id,
						new UserProfile
						{
							UserId = TestData.Users.TestUser1Id,
							MobileNumber = "+15551234567",
							HomeNumber = "+44 1632 960123",
							VoiceCallMobile = false,
							VoiceCallHome = true
						}
					}
				});

				_phoneNumberProcesserMock.Setup(x => x.Process("+44 1632 960123", null))
					.Returns(new PhoneNumberResult { IsValid = true, InternationalNumber = "+441632960123" });

				_outboundVoiceProviderMock
					.Setup(x => x.SendCommunicationTestCallAsync("+441632960123", result.ResponseToken))
					.ReturnsAsync(true);

				var sent = await _communicationTestService.DeliverRunAsync(_runId);

				sent.Should().Be(1);
				_outboundVoiceProviderMock.Verify(x => x.SendCommunicationTestCallAsync("+441632960123", result.ResponseToken), Times.Once);
				_outboundVoiceProviderMock.Verify(x => x.SendCommunicationTestCallAsync("+15551234567", It.IsAny<string>()), Times.Never);
			}

			[Test]
			public async Task should_push_with_a_ct_prefixed_event_code_carrying_the_response_token()
			{
				var result = SetupSingleResult(CommunicationTestChannel.Push, null);

				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>
				{
					{ TestData.Users.TestUser1Id, new UserProfile { UserId = TestData.Users.TestUser1Id, SendNotificationPush = true } }
				});

				StandardPushMessage pushed = null;
				_pushServiceMock
					.Setup(x => x.PushNotification(It.IsAny<StandardPushMessage>(), TestData.Users.TestUser1Id, It.IsAny<UserProfile>()))
					.Callback<StandardPushMessage, string, UserProfile>((m, u, p) => pushed = m)
					.ReturnsAsync(true);

				var sent = await _communicationTestService.DeliverRunAsync(_runId);

				sent.Should().Be(1);
				pushed.Should().NotBeNull();

				// The Responder app matches on the "CT:" prefix and posts the remainder back to
				// RecordPushResponse. Changing either half silently kills push confirmation.
				pushed.Id.Should().Be($"CT:{result.ResponseToken}");
				result.SendSucceeded.Should().BeTrue();
			}

			[Test]
			public async Task should_record_a_failed_push_rather_than_claiming_success()
			{
				var result = SetupSingleResult(CommunicationTestChannel.Push, null);

				_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(1, false)).ReturnsAsync(new Dictionary<string, UserProfile>
				{
					{ TestData.Users.TestUser1Id, new UserProfile { UserId = TestData.Users.TestUser1Id, SendNotificationPush = true } }
				});

				_pushServiceMock
					.Setup(x => x.PushNotification(It.IsAny<StandardPushMessage>(), It.IsAny<string>(), It.IsAny<UserProfile>()))
					.ReturnsAsync(false);

				var sent = await _communicationTestService.DeliverRunAsync(_runId);

				sent.Should().Be(0);
				result.SendSucceeded.Should().BeFalse();
			}

			[Test]
			public async Task should_not_send_twice_for_a_result_already_sent()
			{
				var result = SetupSingleResult(CommunicationTestChannel.Email, "user1@test.com");
				result.SentOn = DateTime.UtcNow.AddMinutes(-5);

				var sent = await _communicationTestService.DeliverRunAsync(_runId);

				sent.Should().Be(0);
				_emailServiceMock.Verify(
					x => x.SendCommunicationTestEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
					Times.Never);
			}

			[Test]
			public async Task should_not_touch_a_provider_when_the_department_is_blocked_from_broadcasting()
			{
				// A communication test messages every member of a department, so an ungated run is the
				// loudest thing a non-production environment can do.
				const int blockedDepartmentId = 4242;
				var wasBypassed = SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Remove(blockedDepartmentId);

				try
				{
					var runId = Guid.NewGuid();
					var testId = Guid.NewGuid();

					_communicationTestRunRepoMock.Setup(x => x.GetByIdAsync(runId)).ReturnsAsync(new CommunicationTestRun
					{
						CommunicationTestRunId = runId,
						CommunicationTestId = testId,
						DepartmentId = blockedDepartmentId,
						RunCode = "CT-A7X3",
						Status = (int)CommunicationTestRunStatus.Running
					});
					_communicationTestRunRepoMock
						.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestRun>(), It.IsAny<CancellationToken>(), true))
						.ReturnsAsync((CommunicationTestRun r, CancellationToken c, bool f) => r);

					_communicationTestRepoMock.Setup(x => x.GetByIdAsync(testId)).ReturnsAsync(new CommunicationTest
					{
						CommunicationTestId = testId,
						DepartmentId = blockedDepartmentId,
						Name = "Monthly Check"
					});

					var results = new[]
					{
						CommunicationTestChannel.Email,
						CommunicationTestChannel.Sms,
						CommunicationTestChannel.Voice,
						CommunicationTestChannel.Push
					}.Select(channel => new CommunicationTestResult
					{
						CommunicationTestResultId = Guid.NewGuid(),
						CommunicationTestRunId = runId,
						DepartmentId = blockedDepartmentId,
						UserId = TestData.Users.TestUser1Id,
						Channel = (int)channel,
						ContactValue = "user1@test.com",
						SendAttempted = true,
						ResponseToken = Guid.NewGuid().ToString("N")
					}).ToList();

					_communicationTestResultRepoMock.Setup(x => x.GetResultsByRunIdAsync(runId)).ReturnsAsync(results);
					_communicationTestResultRepoMock
						.Setup(x => x.SaveOrUpdateAsync(It.IsAny<CommunicationTestResult>(), It.IsAny<CancellationToken>(), true))
						.ReturnsAsync((CommunicationTestResult r, CancellationToken c, bool f) => r);

					_departmentsServiceMock.Setup(x => x.GetDepartmentByIdAsync(blockedDepartmentId, It.IsAny<bool>()))
						.ReturnsAsync(new Department { DepartmentId = blockedDepartmentId, Name = "Blocked Dept" });
					_departmentSettingsServiceMock.Setup(x => x.GetTextToCallNumberForDepartmentAsync(blockedDepartmentId)).ReturnsAsync("15550001111");
					_userProfileServiceMock.Setup(x => x.GetAllProfilesForDepartmentAsync(blockedDepartmentId, false)).ReturnsAsync(new Dictionary<string, UserProfile>
					{
						{ TestData.Users.TestUser1Id, new UserProfile { UserId = TestData.Users.TestUser1Id, MembershipEmail = "user1@test.com", MobileNumber = "+15551234567", SendNotificationPush = true } }
					});

					_phoneNumberProcesserMock.Setup(x => x.Process(It.IsAny<string>(), null))
						.Returns(new PhoneNumberResult { IsValid = true, InternationalNumber = "+15551234567" });

					var sent = await _communicationTestService.DeliverRunAsync(runId);

					sent.Should().Be(0);

					_emailServiceMock.Verify(
						x => x.SendCommunicationTestEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
						Times.Never);
					_smsServiceMock.Verify(
						x => x.SendCommunicationTestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MobileCarriers>(), It.IsAny<int>()),
						Times.Never);
					_outboundVoiceProviderMock.Verify(
						x => x.SendCommunicationTestCallAsync(It.IsAny<string>(), It.IsAny<string>()),
						Times.Never);
					_pushServiceMock.Verify(
						x => x.PushNotification(It.IsAny<StandardPushMessage>(), It.IsAny<string>(), It.IsAny<UserProfile>()),
						Times.Never);

					// Recorded as attempted-and-failed rather than left unsent, otherwise the recovery
					// sweep would keep re-processing a run that can never send.
					results.Should().OnlyContain(r => !r.SendSucceeded && r.SentOn.HasValue);
				}
				finally
				{
					if (wasBypassed)
						SystemBehaviorConfig.BypassDoNotBroadcastDepartments.Add(blockedDepartmentId);
				}
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
			public async Task should_match_an_e164_reply_against_a_locally_formatted_contact_value()
			{
				var runId = Guid.NewGuid();
				var run = new CommunicationTestRun
				{
					CommunicationTestRunId = runId,
					Status = (int)CommunicationTestRunStatus.AwaitingResponses,
					RunCode = "CT-B2K9"
				};

				_communicationTestRunRepoMock.Setup(x => x.GetRunByRunCodeAsync("CT-B2K9")).ReturnsAsync(run);

				var results = new List<CommunicationTestResult>
				{
					new CommunicationTestResult
					{
						CommunicationTestResultId = Guid.NewGuid(),
						CommunicationTestRunId = runId,
						UserId = TestData.Users.TestUser1Id,
						Channel = (int)CommunicationTestChannel.Sms,
						ContactValue = "(555) 123-4567",
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

				// The webhook reports the sender in E.164 with the country code the profile omits.
				var success = await _communicationTestService.RecordSmsResponseAsync("CT-B2K9", "15551234567");

				success.Should().BeTrue();
				results[0].Responded.Should().BeTrue();
			}

			[Test]
			public async Task should_not_match_a_reply_against_a_too_short_contact_value()
			{
				var runId = Guid.NewGuid();
				var run = new CommunicationTestRun
				{
					CommunicationTestRunId = runId,
					Status = (int)CommunicationTestRunStatus.AwaitingResponses,
					RunCode = "CT-C3L4"
				};

				_communicationTestRunRepoMock.Setup(x => x.GetRunByRunCodeAsync("CT-C3L4")).ReturnsAsync(run);
				_communicationTestResultRepoMock.Setup(x => x.GetResultsByRunIdAsync(runId)).ReturnsAsync(new List<CommunicationTestResult>
				{
					new CommunicationTestResult
					{
						CommunicationTestResultId = Guid.NewGuid(),
						CommunicationTestRunId = runId,
						UserId = TestData.Users.TestUser1Id,
						Channel = (int)CommunicationTestChannel.Sms,
						ContactValue = "4567",
						SendAttempted = true,
						Responded = false
					}
				});

				var success = await _communicationTestService.RecordSmsResponseAsync("CT-C3L4", "15551234567");

				success.Should().BeFalse();
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
