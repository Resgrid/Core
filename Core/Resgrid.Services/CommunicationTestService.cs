using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using CommunicationTestMessages = Resgrid.Localization.Areas.User.CommunicationTest.CommunicationTestMessageCatalog;
using Resgrid.Model;
using Resgrid.Model.Messages;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CommunicationTestService : ICommunicationTestService
	{
		/// <summary>
		/// How long a run is left alone before the recovery sweep will re-process it, so the sweep
		/// cannot collide with a queue consumer that is still working through the same run.
		/// </summary>
		private static readonly TimeSpan RecoveryGracePeriod = TimeSpan.FromMinutes(30);

		/// <summary>
		/// Width of CommunicationTestResults.StaffingLevelText. A department can name a custom
		/// staffing level anything it likes, so the snapshot is trimmed rather than left to fail the
		/// insert and take the whole run down with it.
		/// </summary>
		private const int StaffingLevelTextMaxLength = 50;

		private readonly ICommunicationTestRepository _communicationTestRepository;
		private readonly ICommunicationTestRunRepository _communicationTestRunRepository;
		private readonly ICommunicationTestResultRepository _communicationTestResultRepository;
		private readonly ICommunicationTestTargetRepository _communicationTestTargetRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUserStateService _userStateService;
		private readonly ICustomStateService _customStateService;
		private readonly ISmsService _smsService;
		private readonly IEmailService _emailService;
		private readonly IPushService _pushService;
		private readonly IOutboundVoiceProvider _outboundVoiceProvider;
		private readonly IPhoneNumberProcesserProvider _phoneNumberProcesser;
		private readonly IQueueService _queueService;
		private readonly IUnitOfWork _unitOfWork;

		public CommunicationTestService(
			ICommunicationTestRepository communicationTestRepository,
			ICommunicationTestRunRepository communicationTestRunRepository,
			ICommunicationTestResultRepository communicationTestResultRepository,
			ICommunicationTestTargetRepository communicationTestTargetRepository,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IDepartmentSettingsService departmentSettingsService,
			IUserStateService userStateService,
			ICustomStateService customStateService,
			ISmsService smsService,
			IEmailService emailService,
			IPushService pushService,
			IOutboundVoiceProvider outboundVoiceProvider,
			IPhoneNumberProcesserProvider phoneNumberProcesser,
			IQueueService queueService,
			IUnitOfWork unitOfWork)
		{
			_communicationTestRepository = communicationTestRepository;
			_communicationTestRunRepository = communicationTestRunRepository;
			_communicationTestResultRepository = communicationTestResultRepository;
			_communicationTestTargetRepository = communicationTestTargetRepository;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_departmentSettingsService = departmentSettingsService;
			_userStateService = userStateService;
			_customStateService = customStateService;
			_smsService = smsService;
			_emailService = emailService;
			_pushService = pushService;
			_outboundVoiceProvider = outboundVoiceProvider;
			_phoneNumberProcesser = phoneNumberProcesser;
			_queueService = queueService;
			_unitOfWork = unitOfWork;
		}

		public async Task<IEnumerable<CommunicationTest>> GetTestsByDepartmentIdAsync(int departmentId)
		{
			return await _communicationTestRepository.GetAllByDepartmentIdAsync(departmentId);
		}

		public async Task<CommunicationTest> GetTestByIdAsync(Guid communicationTestId)
		{
			return await _communicationTestRepository.GetByIdAsync(communicationTestId);
		}

		public async Task<bool> CanCreateScheduledTestAsync(int departmentId, int scheduleType, Guid? excludeTestId = null)
		{
			if (scheduleType == (int)CommunicationTestScheduleType.OnDemand)
				return true;

			var existing = await _communicationTestRepository.GetAllByDepartmentIdAsync(departmentId);
			if (existing == null)
				return true;

			return !existing.Any(t =>
				t.ScheduleType == scheduleType &&
				(!excludeTestId.HasValue || t.CommunicationTestId != excludeTestId.Value));
		}

		public async Task<CommunicationTest> SaveTestAsync(CommunicationTest test, CancellationToken cancellationToken = default)
		{
			return await _communicationTestRepository.SaveOrUpdateAsync(test, cancellationToken, true);
		}

		public async Task<bool> DeleteTestAsync(Guid communicationTestId, CancellationToken cancellationToken = default)
		{
			var test = await _communicationTestRepository.GetByIdAsync(communicationTestId);
			if (test == null)
				return false;

			return await _communicationTestRepository.DeleteAsync(test, cancellationToken);
		}

		#region Targeting

		public async Task<IEnumerable<CommunicationTestTarget>> GetTargetsByTestIdAsync(Guid communicationTestId)
		{
			return await _communicationTestTargetRepository.GetTargetsByTestIdAsync(communicationTestId);
		}

		/// <summary>
		/// Replaces the whole target set for a test. Passing an empty collection clears targeting,
		/// which puts the test back to covering the entire department.
		/// </summary>
		public async Task SaveTargetsAsync(Guid communicationTestId, int departmentId, IEnumerable<CommunicationTestTarget> targets, CancellationToken cancellationToken = default)
		{
			var existing = await _communicationTestTargetRepository.GetTargetsByTestIdAsync(communicationTestId);
			if (existing != null)
			{
				foreach (var target in existing)
					await _communicationTestTargetRepository.DeleteAsync(target, cancellationToken);
			}

			if (targets == null)
				return;

			// De-duplicate so the same group/role/user selected twice doesn't create duplicate rows.
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var target in targets)
			{
				if (target == null || string.IsNullOrWhiteSpace(target.TargetId))
					continue;

				if (!seen.Add($"{target.TargetType}:{target.TargetId.Trim()}"))
					continue;

				await _communicationTestTargetRepository.SaveOrUpdateAsync(new CommunicationTestTarget
				{
					CommunicationTestId = communicationTestId,
					DepartmentId = departmentId,
					TargetType = target.TargetType,
					TargetId = target.TargetId.Trim()
				}, cancellationToken, true);
			}
		}

		/// <summary>
		/// Saves a test and replaces its target set as one unit. Targeting is replaced by clearing
		/// every existing row before writing the new ones, so a failure between the two halves leaves
		/// the test with no targets -- which the resolver reads as "the whole department". A narrowly
		/// targeted test silently widening to everyone is the worst way for this to fail, so both
		/// halves commit together or not at all.
		/// </summary>
		public async Task<CommunicationTest> SaveTestWithTargetsAsync(CommunicationTest test, int departmentId, IEnumerable<CommunicationTestTarget> targets, CancellationToken cancellationToken = default)
		{
			if (test == null)
				return null;

			_unitOfWork.CreateOrGetConnection();
			try
			{
				var saved = await SaveTestAsync(test, cancellationToken);

				// SaveTargetsAsync stamps this id onto every row it writes, so a brand new test can
				// have its targets built by the caller before the test itself has an id.
				await SaveTargetsAsync(saved.CommunicationTestId, departmentId, targets, cancellationToken);

				_unitOfWork.CommitChanges();

				return saved;
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}

		/// <summary>
		/// Resolves the user ids a test covers. Returns <c>null</c> when the test has no targeting,
		/// meaning every member of the department is tested.
		/// </summary>
		public async Task<HashSet<string>> ResolveTargetedUserIdsAsync(Guid communicationTestId, int departmentId)
		{
			var targets = await _communicationTestTargetRepository.GetTargetsByTestIdAsync(communicationTestId);
			var targetList = targets?.ToList();

			if (targetList == null || targetList.Count == 0)
				return null;

			var userIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var target in targetList)
			{
				if (string.IsNullOrWhiteSpace(target.TargetId))
					continue;

				switch ((CommunicationTestTargetType)target.TargetType)
				{
					case CommunicationTestTargetType.Group:
						if (int.TryParse(target.TargetId, out var groupId))
						{
							var groupMembers = await _departmentGroupsService.GetAllMembersForGroupAsync(groupId);
							if (groupMembers != null)
							{
								foreach (var member in groupMembers)
									userIds.Add(member.UserId);
							}
						}
						break;

					case CommunicationTestTargetType.Role:
						if (int.TryParse(target.TargetId, out var roleId))
						{
							var roleMembers = await _personnelRolesService.GetAllMembersOfRoleAsync(roleId);
							if (roleMembers != null)
							{
								foreach (var member in roleMembers)
									userIds.Add(member.UserId);
							}
						}
						break;

					case CommunicationTestTargetType.User:
						userIds.Add(target.TargetId);
						break;
				}
			}

			return userIds;
		}

		/// <summary>
		/// Serializes a resolved audience for storage on the run. An untargeted test is stored as the
		/// JSON literal <c>null</c> rather than a NULL column, which is what lets the builder tell a
		/// snapshot that says "whole department" apart from a run that has no snapshot at all.
		/// </summary>
		private static string SerializeTargetedUserIds(HashSet<string> targetedUserIds)
		{
			return JsonConvert.SerializeObject(targetedUserIds);
		}

		/// <summary>
		/// Reads back the audience snapshot stored on a run. Returns false when the run has no usable
		/// snapshot -- it predates snapshots, or its stored value is unreadable -- which means the
		/// caller has to resolve the test's targeting itself.
		/// </summary>
		private static bool TryReadTargetedUserIds(string snapshot, out HashSet<string> targetedUserIds)
		{
			targetedUserIds = null;

			if (string.IsNullOrWhiteSpace(snapshot))
				return false;

			try
			{
				var userIds = JsonConvert.DeserializeObject<List<string>>(snapshot);

				// A snapshot of "null" is a real answer: the test was untargeted when the run started,
				// so the run covers the whole department.
				if (userIds == null)
					return true;

				// Rebuilt with the resolver's case-insensitive comparer -- a set built straight from
				// deserialized values carries the default ordinal comparer, which would drop members
				// whose stored id casing differs from their department membership row.
				targetedUserIds = new HashSet<string>(userIds, StringComparer.OrdinalIgnoreCase);
				return true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		#endregion Targeting

		public async Task<bool> CanStartOnDemandRunAsync(Guid communicationTestId)
		{
			var existingRuns = await _communicationTestRunRepository.GetRunsByTestIdAsync(communicationTestId);
			if (existingRuns == null || !existingRuns.Any())
				return true;

			var mostRecent = existingRuns.OrderByDescending(r => r.StartedOn).FirstOrDefault();
			if (mostRecent == null)
				return true;

			return mostRecent.StartedOn.AddHours(48) <= DateTime.UtcNow;
		}

		/// <summary>
		/// Claims the run and hands it to the worker. Nothing is built or sent here: a department of
		/// any size is one result row insert and one provider round-trip per recipient per channel,
		/// which the request thread that started the run cannot absorb. The audience the run covers
		/// is resolved and stored on the run, so the worker tests who the run was started for.
		/// </summary>
		public async Task<CommunicationTestRun> StartTestRunAsync(Guid communicationTestId, int departmentId, string initiatedByUserId, CancellationToken cancellationToken = default)
		{
			var test = await _communicationTestRepository.GetByIdAsync(communicationTestId);
			if (test == null)
				return null;

			// Rate limit: on-demand tests can only run once every 48 hours
			if (test.ScheduleType == (int)CommunicationTestScheduleType.OnDemand)
			{
				if (!await CanStartOnDemandRunAsync(communicationTestId))
					return null;
			}

			var runCode = GenerateRunCode();

			// Freeze the audience with the run. Targeting can be edited between the run being queued
			// and the worker picking it up, and a run has to test the people it was started for --
			// otherwise the report describes an audience that was never actually tested. Resolving is
			// a handful of membership reads, not the per-recipient work the worker does.
			var targetedUserIds = await ResolveTargetedUserIdsAsync(communicationTestId, departmentId);

			var run = new CommunicationTestRun
			{
				CommunicationTestId = communicationTestId,
				DepartmentId = departmentId,
				InitiatedByUserId = initiatedByUserId,
				StartedOn = DateTime.UtcNow,
				Status = (int)CommunicationTestRunStatus.Pending,
				RunCode = runCode,
				TotalUsersTested = 0,
				TotalResponses = 0,
				TargetedUserIds = SerializeTargetedUserIds(targetedUserIds)
			};

			run = await _communicationTestRunRepository.SaveOrUpdateAsync(run, cancellationToken, true);

			try
			{
				var enqueued = await _queueService.EnqueueCommunicationTestAsync(new CommunicationTestQueueItem
				{
					DepartmentId = departmentId,
					CommunicationTestRunId = run.CommunicationTestRunId.ToString(),
					CommunicationTestId = communicationTestId.ToString()
				}, cancellationToken);

				if (!enqueued)
					Logging.LogInfo($"CommunicationTest: run {run.CommunicationTestRunId} could not be published to the bus; it stays Pending for the recovery sweep.");
			}
			catch (Exception ex)
			{
				// The run row is saved and Pending, so a broker outage delays the test rather than
				// losing it -- DeliverPendingRunsAsync sweeps it up on the worker's next cycle.
				Logging.LogException(ex);
			}

			return run;
		}

		/// <summary>
		/// Worker-side entry point: builds the result rows for a Pending run, then delivers them.
		/// Safe to call twice for the same run -- building is skipped once results exist and each
		/// result is only sent once.
		/// </summary>
		public async Task ProcessRunAsync(Guid communicationTestRunId, CancellationToken cancellationToken = default)
		{
			await BuildRunResultsAsync(communicationTestRunId, cancellationToken);
			await DeliverRunAsync(communicationTestRunId, cancellationToken);
		}

		/// <summary>
		/// Writes one result row per recipient per enabled channel for the audience the run was
		/// started with. No-ops when the run already has results, so a redelivered queue message
		/// cannot double up the audience.
		/// </summary>
		public async Task<CommunicationTestRun> BuildRunResultsAsync(Guid communicationTestRunId, CancellationToken cancellationToken = default)
		{
			var run = await _communicationTestRunRepository.GetByIdAsync(communicationTestRunId);
			if (run == null)
				return null;

			var test = await _communicationTestRepository.GetByIdAsync(run.CommunicationTestId);
			if (test == null)
				return run;

			var existingResults = await _communicationTestResultRepository.GetResultsByRunIdAsync(communicationTestRunId);
			if (existingResults != null && existingResults.Any())
				return run;

			var communicationTestId = run.CommunicationTestId;
			var departmentId = run.DepartmentId;

			var members = await _departmentsService.GetAllMembersForDepartmentAsync(departmentId);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId);

			// A targeted test only covers the audience snapshotted when the run started, so editing
			// the test's targets while the run sat on the queue cannot change who it tests. A run
			// created before snapshots existed has none and falls back to current targeting.
			// Intersecting against current department membership means a stale group/role/user
			// target can never pull in someone who left.
			if (!TryReadTargetedUserIds(run.TargetedUserIds, out var targetedUserIds))
				targetedUserIds = await ResolveTargetedUserIdsAsync(communicationTestId, departmentId);

			if (targetedUserIds != null)
				members = members.Where(m => targetedUserIds.Contains(m.UserId)).ToList();

			// A communication test only proves something if it behaves like the real thing. The
			// department's Suppress (Mute) Staffing Levels setting is what keeps a dispatch away from
			// someone who is off duty, so a test that ignored it would both report a delivery rate no
			// real dispatch could reach and page every off-duty member of the department to do it.
			var suppressInfo = await _departmentSettingsService.GetDepartmentStaffingSuppressInfoAsync(departmentId);
			var latestStates = BuildLatestStateLookup(await _userStateService.GetLatestStatesForDepartmentAsync(departmentId));
			var staffingNames = await BuildStaffingNameLookupAsync(departmentId);

			int totalUsersTested = 0;

			foreach (var member in members)
			{
				profiles.TryGetValue(member.UserId, out var profile);
				bool userHasResults = false;

				// Snapshotted onto every row this member gets: read back off the live profile and
				// department settings, a report opened next month would describe today's configuration
				// instead of the run it is supposed to be a record of.
				latestStates.TryGetValue(member.UserId, out var memberState);
				int? staffingLevel = memberState?.State;
				var staffingLevelText = ResolveStaffingLevelText(staffingLevel, staffingNames);
				var suppressed = IsStaffingSuppressed(suppressInfo, staffingLevel);

				if (test.TestEmail)
				{
					var emailVerified = profile?.EmailVerified;
					var emailEnabled = IsEmailEnabled(profile);
					var result = new CommunicationTestResult
					{
						CommunicationTestRunId = run.CommunicationTestRunId,
						DepartmentId = departmentId,
						UserId = member.UserId,
						Channel = (int)CommunicationTestChannel.Email,
						ContactValue = profile?.MembershipEmail,
						VerificationStatus = (int)emailVerified.ToVerificationStatus(),
						ChannelEnabled = emailEnabled,
						StaffingLevel = staffingLevel,
						StaffingLevelText = staffingLevelText,
						Suppressed = suppressed,
						SendAttempted = !suppressed
							&& emailVerified.IsContactMethodAllowedForSending()
							&& !string.IsNullOrWhiteSpace(profile?.MembershipEmail)
							&& emailEnabled,
						SendSucceeded = false,
						Responded = false,
						ResponseToken = Guid.NewGuid().ToString("N")
					};

					await _communicationTestResultRepository.SaveOrUpdateAsync(result, cancellationToken, true);
					userHasResults = true;
				}

				if (test.TestSms)
				{
					var mobileVerified = profile?.MobileNumberVerified;
					var carrierName = "";
					if (profile != null && profile.MobileCarrier > 0)
						carrierName = ((MobileCarriers)profile.MobileCarrier).GetDescription();

					var smsEnabled = IsSmsEnabled(profile);
					var result = new CommunicationTestResult
					{
						CommunicationTestRunId = run.CommunicationTestRunId,
						DepartmentId = departmentId,
						UserId = member.UserId,
						Channel = (int)CommunicationTestChannel.Sms,
						ContactValue = profile?.GetPhoneNumber(),
						ContactCarrier = carrierName,
						VerificationStatus = (int)mobileVerified.ToVerificationStatus(),
						ChannelEnabled = smsEnabled,
						StaffingLevel = staffingLevel,
						StaffingLevelText = staffingLevelText,
						Suppressed = suppressed,
						SendAttempted = !suppressed
							&& mobileVerified.IsContactMethodAllowedForSending()
							&& !string.IsNullOrWhiteSpace(profile?.GetPhoneNumber())
							&& smsEnabled,
						SendSucceeded = false,
						Responded = false,
						ResponseToken = Guid.NewGuid().ToString("N")
					};

					await _communicationTestResultRepository.SaveOrUpdateAsync(result, cancellationToken, true);
					userHasResults = true;
				}

				if (test.TestVoice)
				{
					// Honour the user's voice routing preference: the number we would really call for a
					// dispatch is the number the test has to ring, otherwise the report proves nothing.
					var useHome = UsesHomeRoute(profile);

					var voiceNumber = useHome ? profile?.GetHomePhoneNumber() : profile?.GetPhoneNumber();
					var voiceVerified = useHome ? profile?.HomeNumberVerified : profile?.MobileNumberVerified;
					var voiceEnabled = IsVoiceEnabled(profile);

					var result = new CommunicationTestResult
					{
						CommunicationTestRunId = run.CommunicationTestRunId,
						DepartmentId = departmentId,
						UserId = member.UserId,
						Channel = (int)CommunicationTestChannel.Voice,
						ContactValue = voiceNumber,
						VerificationStatus = (int)voiceVerified.ToVerificationStatus(),
						ChannelEnabled = voiceEnabled,
						StaffingLevel = staffingLevel,
						StaffingLevelText = staffingLevelText,
						Suppressed = suppressed,
						SendAttempted = !suppressed
							&& voiceVerified.IsContactMethodAllowedForSending()
							&& !string.IsNullOrWhiteSpace(voiceNumber)
							&& voiceEnabled,
						SendSucceeded = false,
						Responded = false,
						ResponseToken = Guid.NewGuid().ToString("N")
					};

					await _communicationTestResultRepository.SaveOrUpdateAsync(result, cancellationToken, true);
					userHasResults = true;
				}

				if (test.TestPush)
				{
					var pushEnabled = IsPushEnabled(profile);
					var result = new CommunicationTestResult
					{
						CommunicationTestRunId = run.CommunicationTestRunId,
						DepartmentId = departmentId,
						UserId = member.UserId,
						Channel = (int)CommunicationTestChannel.Push,
						VerificationStatus = (int)ContactVerificationStatus.Verified,
						ChannelEnabled = pushEnabled,
						StaffingLevel = staffingLevel,
						StaffingLevelText = staffingLevelText,
						Suppressed = suppressed,
						// The push service silently drops a notification when this opt-in is off, so
						// gate here rather than reporting an attempt that never leaves the process.
						SendAttempted = !suppressed && pushEnabled,
						SendSucceeded = false,
						Responded = false,
						ResponseToken = Guid.NewGuid().ToString("N")
					};

					await _communicationTestResultRepository.SaveOrUpdateAsync(result, cancellationToken, true);
					userHasResults = true;
				}

				if (userHasResults)
					totalUsersTested++;
			}

			run.TotalUsersTested = totalUsersTested;
			run.Status = (int)CommunicationTestRunStatus.Running;
			run = await _communicationTestRunRepository.SaveOrUpdateAsync(run, cancellationToken, true);

			return run;
		}

		#region Delivery

		/// <summary>
		/// Sends the messages for every not-yet-sent result on a run and moves the run to
		/// AwaitingResponses. Safe to call more than once: results that already have a SentOn are
		/// skipped, so a run whose delivery was interrupted can be finished by the worker. Sends
		/// nothing when DoNotBroadcast is on and the run's department is not on the bypass list.
		/// </summary>
		public async Task<int> DeliverRunAsync(Guid communicationTestRunId, CancellationToken cancellationToken = default)
		{
			var run = await _communicationTestRunRepository.GetByIdAsync(communicationTestRunId);
			if (run == null)
				return 0;

			var test = await _communicationTestRepository.GetByIdAsync(run.CommunicationTestId);
			if (test == null)
				return 0;

			var results = await _communicationTestResultRepository.GetResultsByRunIdAsync(communicationTestRunId);
			if (results == null)
				return 0;

			var department = await _departmentsService.GetDepartmentByIdAsync(run.DepartmentId);
			var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(run.DepartmentId);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(run.DepartmentId);

			// Every other outbound path honours the global broadcast kill switch, and a communication
			// test is the one feature whose whole job is to message every member of a department -- so
			// an ungated test run is the loudest thing a non-production environment can do. Result rows
			// are still written and the run still finishes, the provider calls are all that is dropped.
			var canTransmit = ConfigHelper.CanTransmit(run.DepartmentId);
			if (!canTransmit)
				Logging.LogInfo($"CommunicationTest: run {communicationTestRunId} sent nothing, DoNotBroadcast is on and department {run.DepartmentId} is not bypassed.");

			int sent = 0;

			foreach (var result in results)
			{
				// Suppressed is checked as well as SendAttempted: the builder already clears
				// SendAttempted for a muted member, and messaging someone the department has muted is
				// the one failure this feature must not have, so it is gated on both.
				if (!result.SendAttempted || result.Suppressed || result.SentOn.HasValue)
					continue;

				profiles.TryGetValue(result.UserId, out var profile);

				bool succeeded = false;

				// Blocked runs fall straight through to the stamp below with succeeded false. Leaving
				// SentOn null instead would look like an interrupted delivery, and the recovery sweep
				// would keep re-processing a run that can never send.
				if (canTransmit)
				{
					// One channel failing for one person must never abort the rest of the run -- the
					// report is the deliverable, and a thrown provider error would leave it half written.
					try
					{
						// Every message is composed for the recipient, so it renders in THEIR language, not
						// the language of whoever started the run or of the worker thread.
						var culture = profile?.Language;

						switch ((CommunicationTestChannel)result.Channel)
						{
							case CommunicationTestChannel.Email:
								succeeded = await _emailService.SendCommunicationTestEmailAsync(
									result.ContactValue,
									profile?.FirstName ?? string.Empty,
									department?.Name ?? "Your department",
									test.Name,
									BuildEmailConfirmUrl(result.ResponseToken),
									culture);
								break;

							case CommunicationTestChannel.Sms:
								succeeded = await SendTestSmsAsync(result, profile, run.RunCode, test.Name, departmentNumber, run.DepartmentId);
								break;

							case CommunicationTestChannel.Voice:
								succeeded = await SendTestVoiceAsync(result, profile);
								break;

							case CommunicationTestChannel.Push:
								succeeded = await SendTestPushAsync(result, profile, department, test.Name);
								break;
						}
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
						succeeded = false;
					}
				}

				result.SendSucceeded = succeeded;
				result.SentOn = DateTime.UtcNow;

				await _communicationTestResultRepository.SaveOrUpdateAsync(result, cancellationToken, true);

				if (succeeded)
					sent++;
			}

			if (run.Status == (int)CommunicationTestRunStatus.Pending || run.Status == (int)CommunicationTestRunStatus.Running)
			{
				run.Status = (int)CommunicationTestRunStatus.AwaitingResponses;
				await _communicationTestRunRepository.SaveOrUpdateAsync(run, cancellationToken, true);
			}

			return sent;
		}

		/// <summary>
		/// Recovery sweep for runs still sitting in Pending/Running. That is what a run looks like
		/// when its queue message never reached the broker, or when the worker that was delivering it
		/// died part-way through. Building and sending are both idempotent, so re-processing is safe.
		/// </summary>
		public async Task DeliverPendingRunsAsync(CancellationToken cancellationToken = default)
		{
			var openRuns = await _communicationTestRunRepository.GetOpenRunsAsync();
			if (openRuns == null)
				return;

			foreach (var run in openRuns)
			{
				if (run.Status != (int)CommunicationTestRunStatus.Pending && run.Status != (int)CommunicationTestRunStatus.Running)
					continue;

				// Leave a run the worker may still be actively processing alone. Without this the sweep
				// races the queue consumer on a freshly published run, and two builders that both see
				// zero results would each write a full set of rows.
				if (run.StartedOn.Add(RecoveryGracePeriod) > DateTime.UtcNow)
					continue;

				try
				{
					await ProcessRunAsync(run.CommunicationTestRunId, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}
		}

		private async Task<bool> SendTestSmsAsync(CommunicationTestResult result, UserProfile profile, string runCode, string testName, string departmentNumber, int departmentId)
		{
			// The stored ContactValue is the display form (digits only). Providers need E.164, so
			// normalise off the raw profile number the same way contact verification does -- an
			// unnormalised number is rejected by Twilio/SignalWire with "Invalid 'To'".
			var rawNumber = profile?.MobileNumber;
			if (string.IsNullOrWhiteSpace(rawNumber))
				return false;

			var processed = _phoneNumberProcesser.Process(rawNumber);
			if (processed == null || !processed.IsValid || string.IsNullOrWhiteSpace(processed.InternationalNumber))
			{
				Logging.LogInfo($"Communication test SMS skipped for user {result.UserId}: mobile number is not a valid sendable number (needs international format, e.g. +<country code><number>).");
				return false;
			}

			var body = CommunicationTestMessages.BuildSmsBody(testName, runCode, profile?.Language);

			var carrier = profile != null && profile.MobileCarrier > 0 ? (MobileCarriers)profile.MobileCarrier : MobileCarriers.None;

			return await _smsService.SendCommunicationTestAsync(processed.InternationalNumber, body, departmentNumber, carrier, departmentId);
		}

		/// <summary>
		/// Whether a test voice call routes to the home number rather than the mobile. Both the result
		/// row and the call itself pick their number through here, so the number the report shows is
		/// the number that was actually dialled.
		/// </summary>
		private static bool UsesHomeRoute(UserProfile profile)
		{
			return profile != null && !profile.VoiceCallMobile && profile.VoiceCallHome
				&& !string.IsNullOrWhiteSpace(profile.GetHomePhoneNumber());
		}

		private async Task<bool> SendTestVoiceAsync(CommunicationTestResult result, UserProfile profile)
		{
			if (string.IsNullOrWhiteSpace(result.ResponseToken))
				return false;

			// The stored ContactValue is the display form: GetPhoneNumber/GetHomePhoneNumber strip the
			// leading "+", which leaves an international number unparseable and fails every non-US voice
			// test as "not a valid sendable number". Normalise off the raw profile number instead, the
			// same way the SMS path does.
			var rawNumber = UsesHomeRoute(profile) ? profile?.HomeNumber : profile?.MobileNumber;
			if (string.IsNullOrWhiteSpace(rawNumber))
				return false;

			var processed = _phoneNumberProcesser.Process(rawNumber);
			if (processed == null || !processed.IsValid || string.IsNullOrWhiteSpace(processed.InternationalNumber))
			{
				Logging.LogInfo($"Communication test voice call skipped for user {result.UserId}: phone number is not a valid sendable number.");
				return false;
			}

			return await _outboundVoiceProvider.SendCommunicationTestCallAsync(processed.InternationalNumber, result.ResponseToken);
		}

		private async Task<bool> SendTestPushAsync(CommunicationTestResult result, UserProfile profile, Department department, string testName)
		{
			if (profile == null || !profile.SendNotificationPush)
				return false;

			var message = new StandardPushMessage
			{
				Title = CommunicationTestMessages.BuildPushTitle(profile.Language),
				SubTitle = CommunicationTestMessages.BuildPushBody(testName, profile.Language),
				// The event code carries the response token to the device. The Responder app parses
				// the "CT:" prefix, shows a confirm button, and posts the token back to
				// CommunicationTests/RecordPushResponse — without it push has no way to be answered.
				Id = CommunicationTestPushEventCode(result.ResponseToken),
				DepartmentCode = department?.Code,
				DepartmentId = result.DepartmentId
			};

			return await _pushService.PushNotification(message, result.UserId, profile);
		}

		/// <summary>
		/// Push event code for a communication test. The "CT:" prefix is what the Responder app
		/// matches on to show the confirm-receipt prompt, and the token after it is what the app
		/// posts back. Changing either half breaks push confirmation, so both sides reference this
		/// format rather than building the string inline.
		/// </summary>
		private static string CommunicationTestPushEventCode(string responseToken)
		{
			return $"CT:{responseToken}";
		}

		private static string BuildEmailConfirmUrl(string responseToken)
		{
			return $"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/CommunicationTestResponse/EmailConfirm?token={Uri.EscapeDataString(responseToken ?? string.Empty)}";
		}

		// A channel counts as reachable when ANY of the per-event opt-ins for it is on. The test
		// answers "can we get a message to this person at all", not "would this specific event type
		// go out", so gating on a single flag would report false negatives.
		private static bool IsSmsEnabled(UserProfile profile)
			=> profile != null && (profile.SendSms || profile.SendMessageSms || profile.SendNotificationSms);

		private static bool IsEmailEnabled(UserProfile profile)
			=> profile != null && (profile.SendEmail || profile.SendMessageEmail || profile.SendNotificationEmail);

		private static bool IsVoiceEnabled(UserProfile profile)
			=> profile != null && profile.VoiceForCall;

		private static bool IsPushEnabled(UserProfile profile)
			=> profile != null && profile.SendNotificationPush;

		/// <summary>
		/// Newest staffing state per member. Folds on the timestamp rather than trusting the list to
		/// already hold one row per user, so a department that comes back with more than one still
		/// resolves to the level the run should see.
		/// </summary>
		private static Dictionary<string, UserState> BuildLatestStateLookup(List<UserState> states)
		{
			var lookup = new Dictionary<string, UserState>(StringComparer.OrdinalIgnoreCase);

			if (states == null)
				return lookup;

			foreach (var state in states)
			{
				if (state == null || string.IsNullOrWhiteSpace(state.UserId))
					continue;

				if (!lookup.TryGetValue(state.UserId, out var existing) || state.Timestamp > existing.Timestamp)
					lookup[state.UserId] = state;
			}

			return lookup;
		}

		/// <summary>
		/// Staffing level id to display name for a department, using its configured staffing levels
		/// and falling back to the Resgrid defaults. Built once per run: resolving per member would be
		/// a lookup per person, and a level renamed mid-run would make two rows of the same report
		/// disagree about what the same number means.
		/// </summary>
		private async Task<Dictionary<int, string>> BuildStaffingNameLookupAsync(int departmentId)
		{
			var names = new Dictionary<int, string>();

			var details = await _customStateService.GetCustomPersonnelStaffingsOrDefaultsAsync(departmentId);
			if (details == null)
				return names;

			foreach (var detail in details)
			{
				if (detail == null || string.IsNullOrWhiteSpace(detail.ButtonText))
					continue;

				names[detail.CustomStateDetailId] = detail.ButtonText;
			}

			return names;
		}

		/// <summary>
		/// Display name to record for a staffing level. A level the department has since deleted --
		/// or one that was never in its configured set -- still has to say something, and the raw
		/// number is the only honest thing left to show.
		/// </summary>
		private static string ResolveStaffingLevelText(int? staffingLevel, Dictionary<int, string> staffingNames)
		{
			if (!staffingLevel.HasValue)
				return null;

			if (staffingNames != null && staffingNames.TryGetValue(staffingLevel.Value, out var name) && !string.IsNullOrWhiteSpace(name))
				return name.Length > StaffingLevelTextMaxLength ? name.Substring(0, StaffingLevelTextMaxLength) : name;

			return staffingLevel.Value.ToString();
		}

		/// <summary>
		/// Whether the department's Suppress (Mute) Staffing Levels setting mutes a member sitting on
		/// this staffing level. Mirrors CommunicationService.CanSendToUser, including its treatment of
		/// a member with no recorded state: there is no level to match against, so they are not muted.
		/// </summary>
		private static bool IsStaffingSuppressed(DepartmentSuppressStaffingInfo suppressInfo, int? staffingLevel)
		{
			if (suppressInfo == null || !suppressInfo.EnableSupressStaffing || suppressInfo.StaffingLevelsToSupress == null)
				return false;

			return staffingLevel.HasValue && suppressInfo.StaffingLevelsToSupress.Contains(staffingLevel.Value);
		}

		#endregion Delivery

		public async Task<IEnumerable<CommunicationTestRun>> GetRunsByTestIdAsync(Guid communicationTestId)
		{
			return await _communicationTestRunRepository.GetRunsByTestIdAsync(communicationTestId);
		}

		public async Task<CommunicationTestRun> GetRunByIdAsync(Guid communicationTestRunId)
		{
			return await _communicationTestRunRepository.GetByIdAsync(communicationTestRunId);
		}

		public async Task<IEnumerable<CommunicationTestRun>> GetRunsByDepartmentIdAsync(int departmentId)
		{
			return await _communicationTestRunRepository.GetAllByDepartmentIdAsync(departmentId);
		}

		public async Task<IEnumerable<CommunicationTestResult>> GetResultsByRunIdAsync(Guid communicationTestRunId)
		{
			return await _communicationTestResultRepository.GetResultsByRunIdAsync(communicationTestRunId);
		}

		public async Task<bool> RecordSmsResponseAsync(string runCode, string fromPhoneNumber)
		{
			var run = await _communicationTestRunRepository.GetRunByRunCodeAsync(runCode);
			if (run == null || run.Status == (int)CommunicationTestRunStatus.Completed || run.Status == (int)CommunicationTestRunStatus.Failed)
				return false;

			var results = await _communicationTestResultRepository.GetResultsByRunIdAsync(run.CommunicationTestRunId);
			var inboundDigits = DigitsOnly(fromPhoneNumber);

			if (string.IsNullOrWhiteSpace(inboundDigits))
				return false;

			var matchingResult = results.FirstOrDefault(r =>
				r.Channel == (int)CommunicationTestChannel.Sms &&
				!r.Responded &&
				IsSamePhoneNumber(r.ContactValue, inboundDigits));

			if (matchingResult == null)
				return false;

			matchingResult.Responded = true;
			matchingResult.RespondedOn = DateTime.UtcNow;
			await _communicationTestResultRepository.SaveOrUpdateAsync(matchingResult, CancellationToken.None, true);

			await UpdateRunResponseCountAsync(run);
			return true;
		}

		public async Task<bool> RecordEmailResponseAsync(string responseToken)
		{
			return await RecordResponseByTokenAsync(responseToken, CommunicationTestChannel.Email);
		}

		public async Task<bool> RecordVoiceResponseAsync(string responseToken)
		{
			return await RecordResponseByTokenAsync(responseToken, CommunicationTestChannel.Voice);
		}

		public async Task<bool> RecordPushResponseAsync(string responseToken)
		{
			return await RecordResponseByTokenAsync(responseToken, CommunicationTestChannel.Push);
		}

		public async Task<int?> GetDepartmentIdByResponseTokenAsync(string responseToken)
		{
			if (string.IsNullOrWhiteSpace(responseToken))
				return null;

			var result = await _communicationTestResultRepository.GetResultByResponseTokenAsync(responseToken);
			return result?.DepartmentId;
		}

		public async Task<string> GetRecipientLanguageByResponseTokenAsync(string responseToken)
		{
			if (string.IsNullOrWhiteSpace(responseToken))
				return null;

			// The voice webhook only carries the token, so the person being called is identified by
			// the result row it belongs to. Their profile language decides what the call says.
			var result = await _communicationTestResultRepository.GetResultByResponseTokenAsync(responseToken);
			if (result == null || string.IsNullOrWhiteSpace(result.UserId))
				return null;

			var profile = await _userProfileService.GetProfileByUserIdAsync(result.UserId);
			return profile?.Language;
		}

		public async Task ProcessScheduledTestsAsync(CancellationToken cancellationToken = default)
		{
			var now = DateTime.UtcNow;

			// Process weekly tests
			var weeklyTests = await _communicationTestRepository.GetActiveTestsForScheduleTypeAsync((int)CommunicationTestScheduleType.Weekly);
			if (weeklyTests != null)
			{
				foreach (var test in weeklyTests)
				{
					if (ShouldRunWeeklyTest(test, now) && await HasPassedFirstEligiblePeriodAsync(test))
					{
						await StartTestRunAsync(test.CommunicationTestId, test.DepartmentId, test.CreatedByUserId, cancellationToken);
					}
				}
			}

			// Process monthly tests
			var monthlyTests = await _communicationTestRepository.GetActiveTestsForScheduleTypeAsync((int)CommunicationTestScheduleType.Monthly);
			if (monthlyTests != null)
			{
				foreach (var test in monthlyTests)
				{
					if (ShouldRunMonthlyTest(test, now) && await HasPassedFirstEligiblePeriodAsync(test))
					{
						await StartTestRunAsync(test.CommunicationTestId, test.DepartmentId, test.CreatedByUserId, cancellationToken);
					}
				}
			}
		}

		public async Task CompleteExpiredRunsAsync(CancellationToken cancellationToken = default)
		{
			var openRuns = await _communicationTestRunRepository.GetOpenRunsAsync();
			if (openRuns == null)
				return;

			foreach (var run in openRuns)
			{
				var test = await _communicationTestRepository.GetByIdAsync(run.CommunicationTestId);
				if (test == null)
					continue;

				var windowMinutes = test.ResponseWindowMinutes > 0 ? test.ResponseWindowMinutes : 60;
				if (run.StartedOn.AddMinutes(windowMinutes) <= DateTime.UtcNow)
				{
					run.Status = (int)CommunicationTestRunStatus.Completed;
					run.CompletedOn = DateTime.UtcNow;
					await _communicationTestRunRepository.SaveOrUpdateAsync(run, cancellationToken, true);
				}
			}
		}

		private async Task<bool> RecordResponseByTokenAsync(string responseToken, CommunicationTestChannel channel)
		{
			var result = await _communicationTestResultRepository.GetResultByResponseTokenAsync(responseToken);
			if (result == null || result.Responded || result.Channel != (int)channel)
				return false;

			result.Responded = true;
			result.RespondedOn = DateTime.UtcNow;
			await _communicationTestResultRepository.SaveOrUpdateAsync(result, CancellationToken.None, true);

			var run = await _communicationTestRunRepository.GetByIdAsync(result.CommunicationTestRunId);
			if (run != null)
				await UpdateRunResponseCountAsync(run);

			return true;
		}

		private async Task UpdateRunResponseCountAsync(CommunicationTestRun run)
		{
			var allResults = await _communicationTestResultRepository.GetResultsByRunIdAsync(run.CommunicationTestRunId);
			var respondedUsers = allResults.Where(r => r.Responded).Select(r => r.UserId).Distinct().Count();
			run.TotalResponses = respondedUsers;
			await _communicationTestRunRepository.SaveOrUpdateAsync(run, CancellationToken.None, true);
		}

		private static string DigitsOnly(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return string.Empty;

			var builder = new StringBuilder(value.Length);
			foreach (var character in value)
			{
				if (char.IsDigit(character))
					builder.Append(character);
			}

			return builder.ToString();
		}

		/// <summary>
		/// Compares two phone numbers by their digits, tolerating a country code on one side only.
		/// The stored contact value is the profile's display form while the inbound webhook reports
		/// an E.164 number, so an exact compare would miss every international sender.
		/// </summary>
		private static bool IsSamePhoneNumber(string storedNumber, string inboundDigits)
		{
			var storedDigits = DigitsOnly(storedNumber);

			if (string.IsNullOrWhiteSpace(storedDigits) || string.IsNullOrWhiteSpace(inboundDigits))
				return false;

			if (storedDigits == inboundDigits)
				return true;

			// Require a meaningful overlap before accepting a suffix match, otherwise short or
			// partially entered numbers could match the wrong member of the department.
			const int minimumSignificantDigits = 7;
			var shortest = Math.Min(storedDigits.Length, inboundDigits.Length);
			if (shortest < minimumSignificantDigits)
				return false;

			return storedDigits.EndsWith(inboundDigits, StringComparison.Ordinal)
				|| inboundDigits.EndsWith(storedDigits, StringComparison.Ordinal);
		}

		private static string GenerateRunCode()
		{
			const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
			var random = new Random();
			var code = new char[4];
			for (int i = 0; i < 4; i++)
				code[i] = chars[random.Next(chars.Length)];
			return "CT-" + new string(code);
		}

		private static bool ShouldRunWeeklyTest(CommunicationTest test, DateTime utcNow)
		{
			switch (utcNow.DayOfWeek)
			{
				case DayOfWeek.Sunday: return test.Sunday;
				case DayOfWeek.Monday: return test.Monday;
				case DayOfWeek.Tuesday: return test.Tuesday;
				case DayOfWeek.Wednesday: return test.Wednesday;
				case DayOfWeek.Thursday: return test.Thursday;
				case DayOfWeek.Friday: return test.Friday;
				case DayOfWeek.Saturday: return test.Saturday;
				default: return false;
			}
		}

		private static bool ShouldRunMonthlyTest(CommunicationTest test, DateTime utcNow)
		{
			return test.DayOfMonth.HasValue && test.DayOfMonth.Value == utcNow.Day;
		}

		/// <summary>
		/// Ensures the first run of a scheduled test happens in the NEXT eligible period
		/// after creation, not the same week/month. This prevents users from abusing
		/// scheduled tests to send immediately.
		/// </summary>
		private async Task<bool> HasPassedFirstEligiblePeriodAsync(CommunicationTest test)
		{
			var existingRuns = await _communicationTestRunRepository.GetRunsByTestIdAsync(test.CommunicationTestId);
			if (existingRuns != null && existingRuns.Any())
				return true; // Already ran before, normal schedule applies

			// First run ever — must be at least one full period after creation
			if (test.ScheduleType == (int)CommunicationTestScheduleType.Weekly)
			{
				// Must be at least 7 days after creation
				return test.CreatedOn.AddDays(7) <= DateTime.UtcNow;
			}
			else if (test.ScheduleType == (int)CommunicationTestScheduleType.Monthly)
			{
				// Must be at least 28 days after creation (minimum month gap)
				return test.CreatedOn.AddDays(28) <= DateTime.UtcNow;
			}

			return true;
		}
	}
}
