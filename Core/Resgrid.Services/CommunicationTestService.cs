using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CommunicationTestService : ICommunicationTestService
	{
		private readonly ICommunicationTestRepository _communicationTestRepository;
		private readonly ICommunicationTestRunRepository _communicationTestRunRepository;
		private readonly ICommunicationTestResultRepository _communicationTestResultRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;

		public CommunicationTestService(
			ICommunicationTestRepository communicationTestRepository,
			ICommunicationTestRunRepository communicationTestRunRepository,
			ICommunicationTestResultRepository communicationTestResultRepository,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService)
		{
			_communicationTestRepository = communicationTestRepository;
			_communicationTestRunRepository = communicationTestRunRepository;
			_communicationTestResultRepository = communicationTestResultRepository;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
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

			var run = new CommunicationTestRun
			{
				CommunicationTestId = communicationTestId,
				DepartmentId = departmentId,
				InitiatedByUserId = initiatedByUserId,
				StartedOn = DateTime.UtcNow,
				Status = (int)CommunicationTestRunStatus.Running,
				RunCode = runCode,
				TotalUsersTested = 0,
				TotalResponses = 0
			};

			run = await _communicationTestRunRepository.SaveOrUpdateAsync(run, cancellationToken, true);

			var members = await _departmentsService.GetAllMembersForDepartmentAsync(departmentId);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId);

			int totalUsersTested = 0;

			foreach (var member in members)
			{
				profiles.TryGetValue(member.UserId, out var profile);
				bool userHasResults = false;

				if (test.TestEmail)
				{
					var emailVerified = profile?.EmailVerified;
					var result = new CommunicationTestResult
					{
						CommunicationTestRunId = run.CommunicationTestRunId,
						DepartmentId = departmentId,
						UserId = member.UserId,
						Channel = (int)CommunicationTestChannel.Email,
						ContactValue = profile?.MembershipEmail,
						VerificationStatus = (int)emailVerified.ToVerificationStatus(),
						SendAttempted = emailVerified.IsContactMethodAllowedForSending(),
						SendSucceeded = false,
						Responded = false,
						ResponseToken = Guid.NewGuid().ToString("N")
					};

					if (result.SendAttempted && !string.IsNullOrWhiteSpace(result.ContactValue))
					{
						result.SendSucceeded = true;
						result.SentOn = DateTime.UtcNow;
					}

					await _communicationTestResultRepository.SaveOrUpdateAsync(result, cancellationToken, true);
					userHasResults = true;
				}

				if (test.TestSms)
				{
					var mobileVerified = profile?.MobileNumberVerified;
					var carrierName = "";
					if (profile != null && profile.MobileCarrier > 0)
						carrierName = ((MobileCarriers)profile.MobileCarrier).GetDescription();

					var result = new CommunicationTestResult
					{
						CommunicationTestRunId = run.CommunicationTestRunId,
						DepartmentId = departmentId,
						UserId = member.UserId,
						Channel = (int)CommunicationTestChannel.Sms,
						ContactValue = profile?.GetPhoneNumber(),
						ContactCarrier = carrierName,
						VerificationStatus = (int)mobileVerified.ToVerificationStatus(),
						SendAttempted = mobileVerified.IsContactMethodAllowedForSending(),
						SendSucceeded = false,
						Responded = false,
						ResponseToken = Guid.NewGuid().ToString("N")
					};

					if (result.SendAttempted && !string.IsNullOrWhiteSpace(result.ContactValue))
					{
						result.SendSucceeded = true;
						result.SentOn = DateTime.UtcNow;
					}

					await _communicationTestResultRepository.SaveOrUpdateAsync(result, cancellationToken, true);
					userHasResults = true;
				}

				if (test.TestVoice)
				{
					var mobileVerified = profile?.MobileNumberVerified;
					var result = new CommunicationTestResult
					{
						CommunicationTestRunId = run.CommunicationTestRunId,
						DepartmentId = departmentId,
						UserId = member.UserId,
						Channel = (int)CommunicationTestChannel.Voice,
						ContactValue = profile?.GetPhoneNumber(),
						VerificationStatus = (int)mobileVerified.ToVerificationStatus(),
						SendAttempted = mobileVerified.IsContactMethodAllowedForSending(),
						SendSucceeded = false,
						Responded = false,
						ResponseToken = Guid.NewGuid().ToString("N")
					};

					if (result.SendAttempted && !string.IsNullOrWhiteSpace(result.ContactValue))
					{
						result.SendSucceeded = true;
						result.SentOn = DateTime.UtcNow;
					}

					await _communicationTestResultRepository.SaveOrUpdateAsync(result, cancellationToken, true);
					userHasResults = true;
				}

				if (test.TestPush)
				{
					var result = new CommunicationTestResult
					{
						CommunicationTestRunId = run.CommunicationTestRunId,
						DepartmentId = departmentId,
						UserId = member.UserId,
						Channel = (int)CommunicationTestChannel.Push,
						VerificationStatus = (int)ContactVerificationStatus.Verified,
						SendAttempted = true,
						SendSucceeded = true,
						SentOn = DateTime.UtcNow,
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
			run.Status = (int)CommunicationTestRunStatus.AwaitingResponses;
			run = await _communicationTestRunRepository.SaveOrUpdateAsync(run, cancellationToken, true);

			return run;
		}

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
			var cleanPhone = fromPhoneNumber.Replace("+", "").Replace("-", "").Replace(" ", "").Replace("(", "").Replace(")", "");

			var matchingResult = results.FirstOrDefault(r =>
				r.Channel == (int)CommunicationTestChannel.Sms &&
				!r.Responded &&
				r.ContactValue != null &&
				r.ContactValue.Replace("+", "").Replace("-", "").Replace(" ", "").Replace("(", "").Replace(")", "") == cleanPhone);

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
