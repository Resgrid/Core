using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Moderation;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>Department and group-scoped moderation requests across supported content types.</summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ModerationController : V4AuthenticatedApiControllerbase
	{
		private readonly IModerationService _moderationService;
		private readonly IAuthorizationService _authorizationService;

		public ModerationController(IModerationService moderationService, IAuthorizationService authorizationService)
		{
			_moderationService = moderationService;
			_authorizationService = authorizationService;
		}

		/// <summary>Reports an accessible chat message, Message, call note or call image.</summary>
		[HttpPost("Flag")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<ModerationActionResult>> Flag([FromBody] FlagModerationInput input,
			CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid || input == null)
				return BadRequest();

			try
			{
				var report = await _moderationService.FlagAsync(DepartmentId, UserId,
					(ModerationItemType)input.ItemType, input.ItemId, (ModerationReason)input.Reason,
					input.Note, BuildContext("Reporter"), cancellationToken);
				var request = await _moderationService.GetReporterRequestAsync(DepartmentId, UserId,
					(ModerationItemType)input.ItemType, input.ItemId);
				var result = new ModerationActionResult
				{
					Success = report != null,
					Data = ConvertRequest(request, false),
					Status = report != null ? ResponseHelper.Created : ResponseHelper.Failure
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (ArgumentException ex)
			{
				return BadRequest(ex.Message);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		/// <summary>Returns the caller's private flag status for one content item.</summary>
		[HttpGet("GetMyStatus")]
		public async Task<ActionResult<GetModerationRequestResult>> GetMyStatus(int itemType, string itemId)
		{
			if (!Enum.IsDefined(typeof(ModerationItemType), itemType) || string.IsNullOrWhiteSpace(itemId))
				return BadRequest();

			var request = await _moderationService.GetReporterRequestAsync(DepartmentId, UserId,
				(ModerationItemType)itemType, itemId);
			if (request == null)
				return NotFound();

			var result = new GetModerationRequestResult
			{
				Data = ConvertRequest(request, false),
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Scoped dashboard/report search for pending and completed moderation requests.</summary>
		[HttpGet("GetRequests")]
		public async Task<ActionResult<GetModerationRequestsResult>> GetRequests(int? status = null,
			int? itemType = null, string contentAuthorUserId = null, string reportedByUserId = null,
			DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 50)
		{
			if (!await _moderationService.CanModerateAsync(DepartmentId, UserId))
				return Unauthorized();

			if (status.HasValue && !Enum.IsDefined(typeof(ModerationRequestStatus), status.Value))
				return BadRequest();
			if (itemType.HasValue && !Enum.IsDefined(typeof(ModerationItemType), itemType.Value))
				return BadRequest();

			var requests = await _moderationService.SearchRequestsAsync(DepartmentId, UserId,
				new ModerationSearchCriteria
				{
					Status = status.HasValue ? (ModerationRequestStatus?)status.Value : null,
					ItemType = itemType.HasValue ? (ModerationItemType?)itemType.Value : null,
					ContentAuthorUserId = contentAuthorUserId,
					ReportedByUserId = reportedByUserId,
					From = from,
					To = to,
					Page = page,
					PageSize = pageSize
				});

			var result = new GetModerationRequestsResult
			{
				Data = requests.Select(x => ConvertRequest(x, true)).ToList(),
				Page = Math.Max(page, 1),
				PageSize = requests.Count,
				Status = requests.Count > 0 ? ResponseHelper.Success : ResponseHelper.NotFound
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Returns a scoped moderation request and its complete action history.</summary>
		[HttpGet("GetRequest")]
		public async Task<ActionResult<GetModerationRequestResult>> GetRequest(string requestId)
		{
			if (!await _moderationService.CanModerateAsync(DepartmentId, UserId))
				return Unauthorized();

			var request = await _moderationService.GetRequestAsync(requestId, DepartmentId, UserId);
			if (request == null)
				return NotFound();

			var result = new GetModerationRequestResult
			{
				Data = ConvertRequest(request, true),
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Completes a scoped request with no action or by removing the live content.</summary>
		[HttpPost("Complete")]
		public async Task<ActionResult<ModerationActionResult>> Complete(string requestId,
			[FromBody] CompleteModerationInput input, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid || input == null || string.IsNullOrWhiteSpace(requestId))
				return BadRequest();
			if (!await _moderationService.CanModerateAsync(DepartmentId, UserId))
				return Unauthorized();

			var isDepartmentAdmin = await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId);
			try
			{
				var request = await _moderationService.CompleteRequestAsync(requestId, DepartmentId, UserId,
					(ModerationDisposition)input.Disposition, input.AdminNote,
					BuildContext(isDepartmentAdmin ? "DepartmentAdmin" : "GroupAdmin"), cancellationToken);
				if (request == null)
					return NotFound();

				var result = new ModerationActionResult
				{
					Success = true,
					Data = ConvertRequest(request, true),
					Status = ResponseHelper.Updated
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		/// <summary>Downloads permanently retained image evidence for an authorized administrator.</summary>
		[HttpGet("DownloadEvidence")]
		public async Task<IActionResult> DownloadEvidence(string requestId)
		{
			if (!await _moderationService.CanModerateAsync(DepartmentId, UserId))
				return Unauthorized();

			var request = await _moderationService.GetRequestAsync(requestId, DepartmentId, UserId);
			if (request?.OriginalContent == null)
				return NotFound();

			var isDepartmentAdmin = await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId);
			await _moderationService.RecordEvidenceAccessAsync(requestId, DepartmentId, UserId,
				BuildContext(isDepartmentAdmin ? "DepartmentAdmin" : "GroupAdmin"));

			return File(request.OriginalContent,
				string.IsNullOrWhiteSpace(request.OriginalContentType) ? "application/octet-stream" : request.OriginalContentType,
				string.IsNullOrWhiteSpace(request.OriginalFileName) ? $"moderation-evidence-{requestId}" : request.OriginalFileName);
		}

		private ChatModerationContext BuildContext(string actorRole)
		{
			return new ChatModerationContext
			{
				IpAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString(),
				UserAgent = Request?.Headers != null ? Request.Headers["User-Agent"].ToString() : null,
				TraceId = HttpContext?.TraceIdentifier,
				ActorRole = actorRole
			};
		}

		private static ModerationRequestResultData ConvertRequest(ModerationRequest request, bool includeAudit)
		{
			if (request == null)
				return null;

			return new ModerationRequestResultData
			{
				ModerationRequestId = request.ModerationRequestId,
				ItemType = request.ItemType,
				ItemId = request.ItemId,
				CallId = request.CallId,
				ChatChannelId = request.ChatChannelId,
				ContentAuthorUserId = request.ContentAuthorUserId,
				ContentAuthorUnitId = request.ContentAuthorUnitId,
				ContentCreatedOn = request.ContentCreatedOn,
				OriginalSubject = request.OriginalSubject,
				OriginalText = request.OriginalText,
				OriginalFileName = request.OriginalFileName,
				OriginalContentType = request.OriginalContentType,
				HasOriginalContent = request.OriginalContent != null || !string.IsNullOrWhiteSpace(request.OriginalFileName),
				OriginalMetadataJson = request.OriginalMetadataJson,
				Status = request.Status,
				Disposition = request.Disposition,
				CreatedOn = request.CreatedOn,
				ModifiedOn = request.ModifiedOn,
				CompletedByUserId = request.CompletedByUserId,
				CompletedOn = request.CompletedOn,
				AdminNote = request.AdminNote,
				Reports = request.Reports.Select(x => new ModerationReportResultData
				{
					ModerationReportId = x.ModerationReportId,
					ReportedByUserId = x.ReportedByUserId,
					ReporterGroupId = x.ReporterGroupId,
					Reason = x.Reason,
					Note = x.Note,
					ReportedOn = x.ReportedOn
				}).ToList(),
				Actions = includeAudit
					? request.Actions.Select(x => new ModerationActionResultData
					{
						ModerationActionId = x.ModerationActionId,
						ActionType = x.ActionType,
						PerformedByUserId = x.PerformedByUserId,
						PerformedOn = x.PerformedOn,
						Note = x.Note,
						PreviousStatus = x.PreviousStatus,
						NewStatus = x.NewStatus,
						ActorRole = x.ActorRole,
						IpAddress = x.IpAddress,
						UserAgent = x.UserAgent,
						TraceId = x.TraceId,
						ServerName = x.ServerName,
						DetailsJson = x.DetailsJson,
						HasEvidence = !string.IsNullOrWhiteSpace(x.EvidenceText) || !string.IsNullOrWhiteSpace(x.EvidenceMetadataJson)
					}).ToList()
					: new System.Collections.Generic.List<ModerationActionResultData>()
			};
		}
	}
}
