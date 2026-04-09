using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.CommunicationTests;
using Resgrid.Web.ServicesCore.Helpers;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Communication Test management - CRUD, run tests, get reports. Admin only.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CommunicationTestsController : V4AuthenticatedApiControllerbase
	{
		private readonly ICommunicationTestService _communicationTestService;
		private readonly IUserProfileService _userProfileService;
		private readonly IEventAggregator _eventAggregator;

		public CommunicationTestsController(
			ICommunicationTestService communicationTestService,
			IUserProfileService userProfileService,
			IEventAggregator eventAggregator)
		{
			_communicationTestService = communicationTestService;
			_userProfileService = userProfileService;
			_eventAggregator = eventAggregator;
		}

		/// <summary>
		/// Gets all communication tests for the current department
		/// </summary>
		[HttpGet("GetAll")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CommunicationTest_View)]
		public async Task<GetCommunicationTestsResult> GetAll()
		{
			var result = new GetCommunicationTestsResult();

			var tests = await _communicationTestService.GetTestsByDepartmentIdAsync(DepartmentId);
			if (tests != null)
			{
				foreach (var test in tests)
				{
					result.Data.Add(new CommunicationTestData
					{
						Id = test.CommunicationTestId.ToString(),
						Name = test.Name,
						Description = test.Description,
						ScheduleType = test.ScheduleType,
						Sunday = test.Sunday,
						Monday = test.Monday,
						Tuesday = test.Tuesday,
						Wednesday = test.Wednesday,
						Thursday = test.Thursday,
						Friday = test.Friday,
						Saturday = test.Saturday,
						DayOfMonth = test.DayOfMonth,
						Time = test.Time,
						TestSms = test.TestSms,
						TestEmail = test.TestEmail,
						TestVoice = test.TestVoice,
						TestPush = test.TestPush,
						Active = test.Active,
						ResponseWindowMinutes = test.ResponseWindowMinutes,
						CreatedOn = test.CreatedOn.ToString("O")
					});
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets a specific communication test by id
		/// </summary>
		[HttpGet("Get")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CommunicationTest_View)]
		public async Task<GetCommunicationTestResult> Get(string id)
		{
			var result = new GetCommunicationTestResult();

			if (!Guid.TryParse(id, out var testId))
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var test = await _communicationTestService.GetTestByIdAsync(testId);
			if (test == null || test.DepartmentId != DepartmentId)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = new CommunicationTestData
			{
				Id = test.CommunicationTestId.ToString(),
				Name = test.Name,
				Description = test.Description,
				ScheduleType = test.ScheduleType,
				Sunday = test.Sunday,
				Monday = test.Monday,
				Tuesday = test.Tuesday,
				Wednesday = test.Wednesday,
				Thursday = test.Thursday,
				Friday = test.Friday,
				Saturday = test.Saturday,
				DayOfMonth = test.DayOfMonth,
				Time = test.Time,
				TestSms = test.TestSms,
				TestEmail = test.TestEmail,
				TestVoice = test.TestVoice,
				TestPush = test.TestPush,
				Active = test.Active,
				ResponseWindowMinutes = test.ResponseWindowMinutes,
				CreatedOn = test.CreatedOn.ToString("O")
			};

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Saves a communication test definition (admin only).
		/// Creates require CommunicationTest_Create; updates require CommunicationTest_Update.
		/// </summary>
		[HttpPost("Save")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CommunicationTest_Create)]
		public async Task<SaveCommunicationTestResult> Save([FromBody] SaveCommunicationTestInput input, CancellationToken cancellationToken)
		{
			var result = new SaveCommunicationTestResult();
			bool isNew = true;
			string beforeJson = null;

			Guid? excludeId = null;
			if (!string.IsNullOrWhiteSpace(input.Id) && Guid.TryParse(input.Id, out var parsedExclude))
				excludeId = parsedExclude;

			if (excludeId.HasValue)
			{
				if (!User.HasClaim(ResgridClaimTypes.Resources.CommunicationTest, ResgridClaimTypes.Actions.Update))
				{
					result.Status = ResponseHelper.Failure;
					ResponseHelper.PopulateV4ResponseData(result);
					return result;
				}
			}

			if (!await _communicationTestService.CanCreateScheduledTestAsync(DepartmentId, input.ScheduleType, excludeId))
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			CommunicationTest test;
			if (excludeId.HasValue)
			{
				test = await _communicationTestService.GetTestByIdAsync(excludeId.Value);
				if (test == null || test.DepartmentId != DepartmentId)
				{
					result.Status = ResponseHelper.NotFound;
					ResponseHelper.PopulateV4ResponseData(result);
					return result;
				}

				isNew = false;
				beforeJson = test.CloneJsonToString();
				test.UpdatedOn = DateTime.UtcNow;
			}
			else
			{
				test = new CommunicationTest
				{
					DepartmentId = DepartmentId,
					CreatedByUserId = UserId,
					CreatedOn = DateTime.UtcNow
				};
			}

			test.Name = input.Name;
			test.Description = input.Description;
			test.ScheduleType = input.ScheduleType;
			test.Sunday = input.Sunday;
			test.Monday = input.Monday;
			test.Tuesday = input.Tuesday;
			test.Wednesday = input.Wednesday;
			test.Thursday = input.Thursday;
			test.Friday = input.Friday;
			test.Saturday = input.Saturday;
			test.DayOfMonth = input.DayOfMonth;
			test.Time = input.Time;
			test.TestSms = input.TestSms;
			test.TestEmail = input.TestEmail;
			test.TestVoice = input.TestVoice;
			test.TestPush = input.TestPush;
			test.Active = input.Active;
			test.ResponseWindowMinutes = input.ResponseWindowMinutes > 0 ? input.ResponseWindowMinutes : 60;

			test = await _communicationTestService.SaveTestAsync(test, cancellationToken);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = isNew ? AuditLogTypes.CommunicationTestCreated : AuditLogTypes.CommunicationTestUpdated,
				Before = beforeJson,
				After = test.CloneJsonToString(),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			result.Id = test.CommunicationTestId.ToString();
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Deletes a communication test (admin only)
		/// </summary>
		[HttpDelete("Delete")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CommunicationTest_Delete)]
		public async Task<StandardApiResponseV4Base> Delete(string id, CancellationToken cancellationToken)
		{
			var result = new StandardApiResponseV4Base();

			if (!Guid.TryParse(id, out var testId))
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var test = await _communicationTestService.GetTestByIdAsync(testId);
			if (test == null || test.DepartmentId != DepartmentId)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var beforeJson = test.CloneJsonToString();

			await _communicationTestService.DeleteTestAsync(testId, cancellationToken);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.CommunicationTestDeleted,
				Before = beforeJson,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			result.Status = ResponseHelper.Deleted;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Starts a new on-demand test run (admin only, rate limited to once per 48 hours)
		/// </summary>
		[HttpPost("StartRun")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CommunicationTest_Create)]
		public async Task<StartTestRunResult> StartRun(string testId, CancellationToken cancellationToken)
		{
			var result = new StartTestRunResult();

			if (!Guid.TryParse(testId, out var id))
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var test = await _communicationTestService.GetTestByIdAsync(id);
			if (test == null || test.DepartmentId != DepartmentId)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			if (test.ScheduleType != (int)CommunicationTestScheduleType.OnDemand)
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			// Check 48-hour rate limit for on-demand tests
			if (!await _communicationTestService.CanStartOnDemandRunAsync(id))
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var run = await _communicationTestService.StartTestRunAsync(id, DepartmentId, UserId, cancellationToken);
			if (run == null)
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.CommunicationTestRunStarted,
				After = run.CloneJsonToString(),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			result.Id = run.CommunicationTestRunId.ToString();
			result.RunCode = run.RunCode;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets test runs for a specific test
		/// </summary>
		[HttpGet("GetRuns")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CommunicationTest_View)]
		public async Task<GetTestRunsResult> GetRuns(string testId)
		{
			var result = new GetTestRunsResult();

			if (!Guid.TryParse(testId, out var id))
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var runs = await _communicationTestService.GetRunsByTestIdAsync(id);
			if (runs != null)
			{
				foreach (var run in runs)
				{
					if (run.DepartmentId != DepartmentId)
						continue;

					result.Data.Add(new TestRunData
					{
						Id = run.CommunicationTestRunId.ToString(),
						CommunicationTestId = run.CommunicationTestId.ToString(),
						StartedOn = run.StartedOn.ToString("O"),
						CompletedOn = run.CompletedOn?.ToString("O"),
						Status = run.Status,
						RunCode = run.RunCode,
						TotalUsersTested = run.TotalUsersTested,
						TotalResponses = run.TotalResponses
					});
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets the report for a specific test run
		/// </summary>
		[HttpGet("GetReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.CommunicationTest_View)]
		public async Task<GetTestRunReportResult> GetReport(string runId)
		{
			var result = new GetTestRunReportResult();

			if (!Guid.TryParse(runId, out var id))
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var run = await _communicationTestService.GetRunByIdAsync(id);
			if (run == null || run.DepartmentId != DepartmentId)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var results = await _communicationTestService.GetResultsByRunIdAsync(id);
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);

			if (results != null)
			{
				foreach (var r in results)
				{
					string userName = r.UserId;
					if (profiles.TryGetValue(r.UserId, out var profile))
						userName = $"{profile.FirstName} {profile.LastName}".Trim();

					result.Data.Add(new CommunicationTestResultData
					{
						Id = r.CommunicationTestResultId.ToString(),
						UserId = r.UserId,
						UserName = userName,
						Channel = r.Channel,
						ContactValue = r.ContactValue,
						ContactCarrier = r.ContactCarrier,
						VerificationStatus = r.VerificationStatus,
						SendAttempted = r.SendAttempted,
						SendSucceeded = r.SendSucceeded,
						SentOn = r.SentOn?.ToString("O"),
						Responded = r.Responded,
						RespondedOn = r.RespondedOn?.ToString("O")
					});
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Records a push notification response for a communication test
		/// </summary>
		[HttpPost("RecordPushResponse")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<RecordPushResponseResult> RecordPushResponse([FromBody] RecordPushResponseInput input)
		{
			var result = new RecordPushResponseResult();

			if (string.IsNullOrWhiteSpace(input?.ResponseToken))
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var success = await _communicationTestService.RecordPushResponseAsync(input.ResponseToken);

			result.Status = success ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}
	}
}
