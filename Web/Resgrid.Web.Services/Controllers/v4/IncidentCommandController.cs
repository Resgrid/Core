using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Filters;
using Resgrid.Web.Services.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using ICModels = Resgrid.Web.Services.Models.v4.IncidentCommand;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Live incident command: establish/transfer/close command, edit the command structure (lanes), assign
	/// resources, manage objectives, timers, map annotations, and read the action timeline for a Call.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class IncidentCommandController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IIncidentCommandService _incidentCommandService;

		public IncidentCommandController(IIncidentCommandService incidentCommandService)
		{
			_incidentCommandService = incidentCommandService;
		}
		#endregion Members and Constructors

		#region Command lifecycle

		/// <summary>Establishes command on a call, optionally seeding lanes from a command definition.</summary>
		// Bootstrap: intentionally NOT [RequiresIncidentCapability]. EstablishCommand CREATES the command, so at call
		// time no command/commander/role (hence no IncidentCapabilities) exists yet — GetCapabilitiesForUserAsync would
		// return None and 403 every establish. Department-level [Authorize(Command_Create)] is the correct gate here;
		// the incident-scoped capability checks apply to the lifecycle endpoints that operate on an existing command.
		[HttpPost("EstablishCommand")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Create)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> EstablishCommand([FromBody] ICModels.EstablishCommandInput input)
		{
			if (input == null || input.CallId <= 0)
				return BadRequest();

			var result = new ICModels.IncidentCommandResult();
			var command = await _incidentCommandService.EstablishCommandAsync(DepartmentId, input.CallId, UserId, input.CommandDefinitionId, CancellationToken.None);

			if (command == null)
			{
				// Call not found / not owned by the caller's department.
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = command;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Gets the most recent command for a call across ALL statuses (unlike GetCommandBoard, which only
		/// resolves the active one). Lets the IC app detect a prior ended command and offer to reopen it.
		/// </summary>
		[HttpGet("GetCommandForCall/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> GetCommandForCall(int callId)
		{
			var result = new ICModels.IncidentCommandResult();
			var command = await _incidentCommandService.GetCommandForCallAsync(DepartmentId, callId);

			if (command == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = command;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Reopens a previously closed command with a reason. Bootstrap-gated like EstablishCommand: after a
		/// close there is no active command, so incident capabilities cannot be evaluated — the department-level
		/// Command_Create claim is the gate.
		/// </summary>
		[HttpPut("ReopenCommand")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Create)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> ReopenCommand([FromBody] ICModels.ReopenCommandInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId))
				return BadRequest();

			var result = new ICModels.IncidentCommandResult();
			try
			{
				var command = await _incidentCommandService.ReopenCommandAsync(DepartmentId, input.IncidentCommandId, input.Reason, UserId, CancellationToken.None);

				if (command == null)
				{
					result.Status = ResponseHelper.NotFound;
					ResponseHelper.PopulateV4ResponseData(result);
					return result;
				}

				result.Data = command;
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		/// <summary>
		/// List-card summaries for the department's commands: duration, resolved commander name, locations, and
		/// active unit/personnel counts. Active only by default; includeClosed=true adds ended incidents.
		/// </summary>
		[HttpGet("GetCommandList")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentCommandSummariesResult>> GetCommandList([FromQuery] bool includeClosed = false)
		{
			var summaries = await _incidentCommandService.GetCommandSummariesForDepartmentAsync(DepartmentId, includeClosed);
			var result = new ICModels.IncidentCommandSummariesResult
			{
				Data = summaries,
				PageSize = summaries.Count,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Board snapshot for one specific command instance — including a CLOSED one (read-only history view for
		/// ended incidents). Child rows are filtered to that command, so a closed board isn't polluted by a newer
		/// command on the same call.
		/// </summary>
		[HttpGet("GetCommandBoardById/{incidentCommandId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentCommandBoardResult>> GetCommandBoardById(string incidentCommandId)
		{
			var result = new ICModels.IncidentCommandBoardResult();
			var board = await _incidentCommandService.GetCommandBoardByIdAsync(DepartmentId, incidentCommandId);

			if (board == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = board;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the full live command board snapshot for a call.</summary>
		[HttpGet("GetCommandBoard/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentCommandBoardResult>> GetCommandBoard(int callId)
		{
			var result = new ICModels.IncidentCommandBoardResult();
			var board = await _incidentCommandService.GetCommandBoardAsync(DepartmentId, callId);

			if (board == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = board;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Transfers command to another user.</summary>
		[HttpPost("TransferCommand")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand)]
		public async Task<ActionResult<ICModels.CommandTransferResult>> TransferCommand([FromBody] ICModels.TransferCommandInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId) || string.IsNullOrWhiteSpace(input.ToUserId))
				return BadRequest();

			var result = new ICModels.CommandTransferResult();
			var transfer = await _incidentCommandService.TransferCommandAsync(DepartmentId, input.IncidentCommandId, UserId, input.ToUserId, input.Notes, CancellationToken.None);

			if (transfer == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = transfer;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Closes command on an incident.</summary>
		[HttpPut("CloseCommand/{incidentCommandId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> CloseCommand(string incidentCommandId)
		{
			var result = new ICModels.IncidentCommandResult();
			var command = await _incidentCommandService.CloseCommandAsync(DepartmentId, incidentCommandId, UserId, CancellationToken.None);

			if (command == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = command;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Updates the incident action plan.</summary>
		[HttpPut("UpdateActionPlan")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> UpdateActionPlan([FromBody] ICModels.UpdateActionPlanInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId))
				return BadRequest();

			var result = new ICModels.IncidentCommandResult();
			var command = await _incidentCommandService.UpdateActionPlanAsync(DepartmentId, input.IncidentCommandId, input.ActionPlan, UserId, CancellationToken.None);

			if (command == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = command;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Updates the command-post location used by maps and incident weather.</summary>
		[HttpPut("UpdateCommandPost")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> UpdateCommandPost([FromBody] ICModels.UpdateCommandPostInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId))
				return BadRequest();

			try
			{
				var command = await _incidentCommandService.UpdateCommandPostAsync(DepartmentId, input.IncidentCommandId, input.Latitude, input.Longitude, UserId, CancellationToken.None);
				var result = new ICModels.IncidentCommandResult
				{
					Data = command,
					PageSize = command == null ? 0 : 1,
					Status = command == null ? ResponseHelper.NotFound : ResponseHelper.Success
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (ArgumentException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		/// <summary>Updates command-level details every resource should see: estimated end and important information.</summary>
		[HttpPut("UpdateCommandDetails")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> UpdateCommandDetails([FromBody] ICModels.UpdateCommandDetailsInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId))
				return BadRequest();

			var command = await _incidentCommandService.UpdateCommandDetailsAsync(DepartmentId, input.IncidentCommandId, input.EstimatedEndOn, input.ImportantInformation, UserId, CancellationToken.None);
			var result = new ICModels.IncidentCommandResult
			{
				Data = command,
				PageSize = command == null ? 0 : 1,
				Status = command == null ? ResponseHelper.NotFound : ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Updates core incident metadata (name, corrected start time, estimated end, important information, ICS
		/// level) and the ICP/HQ, Staging, and Rehab locations. A location whose text is supplied without
		/// coordinates is geocoded server-side on save.
		/// </summary>
		[HttpPut("UpdateCommandInfo")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageCommand)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> UpdateCommandInfo([FromBody] ICModels.UpdateCommandInfoInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId))
				return BadRequest();

			try
			{
				var command = await _incidentCommandService.UpdateCommandInfoAsync(DepartmentId, input.IncidentCommandId, new IncidentCommandInfoUpdate
				{
					Name = input.Name,
					EstablishedOn = input.EstablishedOn,
					EstimatedEndOn = input.EstimatedEndOn,
					ClearEstimatedEndOn = input.ClearEstimatedEndOn,
					ImportantInformation = input.ImportantInformation,
					IcsLevel = input.IcsLevel,
					CommandPostLocationText = input.CommandPostLocationText,
					CommandPostLatitude = input.CommandPostLatitude,
					CommandPostLongitude = input.CommandPostLongitude,
					StagingLocationText = input.StagingLocationText,
					StagingLatitude = input.StagingLatitude,
					StagingLongitude = input.StagingLongitude,
					RehabLocationText = input.RehabLocationText,
					RehabLatitude = input.RehabLatitude,
					RehabLongitude = input.RehabLongitude
				}, UserId, CancellationToken.None);

				var result = new ICModels.IncidentCommandResult
				{
					Data = command,
					PageSize = command == null ? 0 : 1,
					Status = command == null ? ResponseHelper.NotFound : ResponseHelper.Success
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (ArgumentException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		/// <summary>
		/// Read-only incident view for the calling responder (or, with unitId, a unit client): commander
		/// contact, timing, important information, objectives, needs, notes and attachments (visibility-
		/// filtered), plus the caller's own lane assignment with leads and lane objectives. Gated by the
		/// Call resource claim — every dispatched responder/unit can read it, not only command staff.
		/// </summary>
		[HttpGet("GetResourceIncidentView/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ICModels.ResourceIncidentViewResult>> GetResourceIncidentView(int callId, [FromQuery] int? unitId = null)
		{
			var result = new ICModels.ResourceIncidentViewResult();

			// Command staff (any incident capability) also see command-only notes/attachments here.
			var includePrivate = false;
			try
			{
				includePrivate = await _incidentCommandService.GetCapabilitiesForUserAsync(DepartmentId, callId, UserId) != IncidentCapabilities.None;
			}
			catch (Exception ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
			}

			var view = await _incidentCommandService.GetResourceIncidentViewAsync(DepartmentId, callId, UserId, unitId, includePrivate);

			if (view == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = view;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#region Notes and documents

		/// <summary>Adds an internal or public operational status note to the incident.</summary>
		[HttpPost("AddNote")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageNotes)]
		public async Task<ActionResult<ICModels.IncidentNoteResult>> AddNote([FromBody] ICModels.AddIncidentNoteInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId) || string.IsNullOrWhiteSpace(input.Body))
				return BadRequest();

			if (input.Visibility == (int)IncidentContentVisibility.Public && !await HasCapabilityAsync(input.IncidentCommandId, IncidentCapabilities.ManagePublicInformation))
				return Forbid();

			try
			{
				var note = await _incidentCommandService.AddNoteAsync(new IncidentNote
				{
					IncidentCommandId = input.IncidentCommandId,
					DepartmentId = DepartmentId,
					NoteType = input.NoteType,
					Visibility = input.Visibility,
					Title = input.Title,
					Body = input.Body,
					ContainmentPercent = input.ContainmentPercent
				}, UserId, CancellationToken.None);

				var result = new ICModels.IncidentNoteResult
				{
					Data = note,
					PageSize = note == null ? 0 : 1,
					Status = note == null ? ResponseHelper.NotFound : ResponseHelper.Created
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (ArgumentException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		[HttpGet("GetNotes/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentNotesResult>> GetNotes(int callId)
		{
			var notes = await _incidentCommandService.GetNotesForCallAsync(DepartmentId, callId);
			var result = new ICModels.IncidentNotesResult { Data = notes, PageSize = notes.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpDelete("RemoveNote/{incidentNoteId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> RemoveNote(string incidentNoteId)
		{
			var removed = await _incidentCommandService.RemoveNoteAsync(DepartmentId, incidentNoteId, UserId, CancellationToken.None);
			var result = new ICModels.IncidentCommandActionResult { Data = removed, Status = removed ? ResponseHelper.Success : ResponseHelper.NotFound };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Uploads an incident-level internal or public file using multipart/form-data.</summary>
		[HttpPost("AddAttachment")]
		[Consumes("multipart/form-data")]
		[RequestSizeLimit(26_214_400)]
		[RequestFormLimits(MultipartBodyLengthLimit = 26_214_400)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageDocuments)]
		public async Task<ActionResult<ICModels.IncidentAttachmentResult>> AddAttachment([FromForm] ICModels.AddIncidentAttachmentInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId) || input.File == null || input.File.Length == 0)
				return BadRequest();

			if (input.Visibility == (int)IncidentContentVisibility.Public && !await HasCapabilityAsync(input.IncidentCommandId, IncidentCapabilities.ManagePublicInformation))
				return Forbid();

			try
			{
				await using var stream = new MemoryStream();
				await input.File.CopyToAsync(stream, cancellationToken);
				var attachment = await _incidentCommandService.AddAttachmentAsync(new IncidentAttachment
				{
					IncidentCommandId = input.IncidentCommandId,
					DepartmentId = DepartmentId,
					Visibility = input.Visibility,
					FileName = input.File.FileName,
					ContentType = input.File.ContentType,
					Description = input.Description,
					Data = stream.ToArray()
				}, UserId, cancellationToken);

				var result = new ICModels.IncidentAttachmentResult
				{
					Data = attachment,
					PageSize = attachment == null ? 0 : 1,
					Status = attachment == null ? ResponseHelper.NotFound : ResponseHelper.Created
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (ArgumentException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		[HttpGet("GetAttachments/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentAttachmentsResult>> GetAttachments(int callId)
		{
			var attachments = await _incidentCommandService.GetAttachmentsForCallAsync(DepartmentId, callId);
			var result = new ICModels.IncidentAttachmentsResult { Data = attachments, PageSize = attachments.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("DownloadAttachment/{incidentAttachmentId}")]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<IActionResult> DownloadAttachment(string incidentAttachmentId)
		{
			var attachment = await _incidentCommandService.GetAttachmentAsync(DepartmentId, incidentAttachmentId);
			if (attachment?.Data == null)
				return NotFound();

			return File(attachment.Data, attachment.ContentType ?? MediaTypeNames.Application.Octet, attachment.FileName);
		}

		[HttpDelete("RemoveAttachment/{incidentAttachmentId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> RemoveAttachment(string incidentAttachmentId)
		{
			var removed = await _incidentCommandService.RemoveAttachmentAsync(DepartmentId, incidentAttachmentId, UserId, CancellationToken.None);
			var result = new ICModels.IncidentCommandActionResult { Data = removed, Status = removed ? ResponseHelper.Success : ResponseHelper.NotFound };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Notes and documents

		#region Public sharing and weather

		[HttpPost("EnablePublicSharing/{incidentCommandId}")]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManagePublicInformation)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> EnablePublicSharing(string incidentCommandId)
		{
			var command = await _incidentCommandService.EnablePublicSharingAsync(DepartmentId, incidentCommandId, UserId, CancellationToken.None);
			var result = new ICModels.IncidentCommandResult { Data = command, PageSize = command == null ? 0 : 1, Status = command == null ? ResponseHelper.NotFound : ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("DisablePublicSharing/{incidentCommandId}")]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManagePublicInformation)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> DisablePublicSharing(string incidentCommandId)
		{
			var command = await _incidentCommandService.DisablePublicSharingAsync(DepartmentId, incidentCommandId, UserId, CancellationToken.None);
			var result = new ICModels.IncidentCommandResult { Data = command, PageSize = command == null ? 0 : 1, Status = command == null ? ResponseHelper.NotFound : ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetWeather/{callId}")]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentWeatherResult>> GetWeather(int callId, CancellationToken cancellationToken)
		{
			try
			{
				var weather = await _incidentCommandService.GetWeatherForIncidentAsync(DepartmentId, callId, cancellationToken);
				var result = new ICModels.IncidentWeatherResult { Data = weather, PageSize = weather == null ? 0 : 1, Status = weather == null ? ResponseHelper.NotFound : ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		#endregion Public sharing and weather

		/// <summary>Gets the personnel accountability / PAR status (Green/Warning/Critical) for the incident.</summary>
		[HttpGet("GetAccountability/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.CommandAccountabilityResult>> GetAccountability(int callId)
		{
			var result = new ICModels.CommandAccountabilityResult();
			result.Data = await _incidentCommandService.GetAccountabilityForCallAsync(DepartmentId, callId);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Runs a personnel accountability (PAR) sweep for the call, raising workflow + real-time alerts for any
		/// member newly overdue (Critical). Returns the user ids flagged this pass. Idempotent — repeated calls
		/// only re-alert after a member checks in and lapses again.
		/// </summary>
		[HttpPost("EvaluateAccountability/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageAccountability)]
		public async Task<ActionResult<ICModels.EvaluateAccountabilityResult>> EvaluateAccountability(int callId)
		{
			var result = new ICModels.EvaluateAccountabilityResult();
			result.Data = await _incidentCommandService.EvaluateCriticalParAsync(DepartmentId, callId, CancellationToken.None);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Command lifecycle

		#region Structure (lanes)

		/// <summary>Creates or updates a command structure lane.</summary>
		[HttpPost("SaveNode")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageStructure)]
		public async Task<ActionResult<ICModels.CommandNodeResult>> SaveNode([FromBody] CommandStructureNode node)
		{
			if (node == null || string.IsNullOrWhiteSpace(node.IncidentCommandId))
				return BadRequest();

			node.DepartmentId = DepartmentId;

			var result = new ICModels.CommandNodeResult();
			var saved = await _incidentCommandService.SaveNodeAsync(node, UserId, CancellationToken.None);

			if (saved == null)
			{
				// Parent incident command not found / not owned by the caller's department.
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Removes a command structure lane.</summary>
		[HttpDelete("DeleteNode/{commandStructureNodeId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> DeleteNode(string commandStructureNodeId)
		{
			var result = new ICModels.IncidentCommandActionResult();
			result.Data = await _incidentCommandService.DeleteNodeAsync(DepartmentId, commandStructureNodeId, UserId, CancellationToken.None);
			result.Status = result.Data ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Structure (lanes)

		#region Resource assignments

		/// <summary>Assigns a resource (own/mutual-aid/ad-hoc unit or person) to a lane.</summary>
		[HttpPost("AssignResource")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.AssignResources)]
		public async Task<ActionResult<ICModels.ResourceAssignmentResult>> AssignResource([FromBody] ResourceAssignment assignment)
		{
			if (assignment == null || string.IsNullOrWhiteSpace(assignment.IncidentCommandId))
				return BadRequest();

			assignment.DepartmentId = DepartmentId;

			var result = new ICModels.ResourceAssignmentResult();

			Resgrid.Model.ResourceAssignment saved;
			try
			{
				saved = await _incidentCommandService.AssignResourceAsync(assignment, UserId, CancellationToken.None);
			}
			catch (CommandRequirementsNotMetException ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				// The target lane forces unit-type/personnel-role requirements the resource doesn't meet.
				result.Status = ResponseHelper.Failure;
				result.Message = ex.Message;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			if (saved == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.Message = saved.RequirementsWarningMessage; // advisory (non-forced) requirements notice, if any
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Moves a resource assignment to a different lane.</summary>
		[HttpPost("MoveResource")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.ResourceAssignmentResult>> MoveResource([FromBody] ICModels.MoveResourceInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.ResourceAssignmentId) || string.IsNullOrWhiteSpace(input.TargetNodeId))
				return BadRequest();

			var result = new ICModels.ResourceAssignmentResult();

			Resgrid.Model.ResourceAssignment assignment;
			try
			{
				assignment = await _incidentCommandService.MoveResourceAsync(DepartmentId, input.ResourceAssignmentId, input.TargetNodeId, UserId, CancellationToken.None);
			}
			catch (CommandRequirementsNotMetException ex)
			{
				Resgrid.Framework.Logging.LogException(ex);
				// The target lane forces unit-type/personnel-role requirements the resource doesn't meet.
				result.Status = ResponseHelper.Failure;
				result.Message = ex.Message;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			if (assignment == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = assignment;
			result.Message = assignment.RequirementsWarningMessage; // advisory (non-forced) requirements notice, if any
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Releases a resource assignment.</summary>
		[HttpPost("ReleaseResource/{resourceAssignmentId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> ReleaseResource(string resourceAssignmentId)
		{
			var result = new ICModels.IncidentCommandActionResult();
			result.Data = await _incidentCommandService.ReleaseResourceAsync(DepartmentId, resourceAssignmentId, UserId, CancellationToken.None);
			result.Status = result.Data ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Resource assignments

		#region Objectives

		/// <summary>Creates or updates a tactical objective / benchmark.</summary>
		[HttpPost("SaveObjective")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageObjectives)]
		public async Task<ActionResult<ICModels.TacticalObjectiveResult>> SaveObjective([FromBody] TacticalObjective objective)
		{
			if (objective == null || string.IsNullOrWhiteSpace(objective.IncidentCommandId))
				return BadRequest();

			objective.DepartmentId = DepartmentId;

			var result = new ICModels.TacticalObjectiveResult();
			var saved = await _incidentCommandService.SaveObjectiveAsync(objective, UserId, CancellationToken.None);

			if (saved == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Sets a tactical objective's progress (0-100; 100 completes it).</summary>
		[HttpPost("UpdateObjectiveProgress")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.TacticalObjectiveResult>> UpdateObjectiveProgress([FromBody] ICModels.UpdateObjectiveProgressInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.TacticalObjectiveId))
				return BadRequest();

			var result = new ICModels.TacticalObjectiveResult();
			var objective = await _incidentCommandService.UpdateObjectiveProgressAsync(DepartmentId, input.TacticalObjectiveId, input.ProgressPercent, UserId, CancellationToken.None);

			if (objective == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = objective;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Marks a tactical objective complete.</summary>
		[HttpPost("CompleteObjective/{tacticalObjectiveId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.TacticalObjectiveResult>> CompleteObjective(string tacticalObjectiveId, [FromBody] ICModels.CompleteObjectiveInput input = null)
		{
			var result = new ICModels.TacticalObjectiveResult();
			var outcome = input != null && Enum.IsDefined(typeof(TacticalObjectiveOutcome), input.Outcome) ? (TacticalObjectiveOutcome)input.Outcome : TacticalObjectiveOutcome.NotSet;
			var objective = await _incidentCommandService.CompleteObjectiveAsync(DepartmentId, tacticalObjectiveId, UserId, outcome, input?.Note, CancellationToken.None);

			if (objective == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = objective;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Objectives

		#region Needs

		/// <summary>Creates or updates a command-level incident need (resources/logistics/etc.).</summary>
		[HttpPost("SaveNeed")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageObjectives)]
		public async Task<ActionResult<ICModels.IncidentNeedResult>> SaveNeed([FromBody] IncidentNeed need)
		{
			if (need == null || string.IsNullOrWhiteSpace(need.IncidentCommandId) || string.IsNullOrWhiteSpace(need.Name))
				return BadRequest();

			need.DepartmentId = DepartmentId;

			var result = new ICModels.IncidentNeedResult();
			var saved = await _incidentCommandService.SaveNeedAsync(need, UserId, CancellationToken.None);

			if (saved == null)
			{
				// Parent incident command not found / not owned by the caller's department.
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Transitions an incident need's fulfillment status (Open/PartiallyMet/Met/Cancelled).</summary>
		[HttpPost("SetNeedStatus")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentNeedResult>> SetNeedStatus([FromBody] ICModels.SetNeedStatusInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentNeedId) || !Enum.IsDefined(typeof(IncidentNeedStatus), input.Status))
				return BadRequest();

			var result = new ICModels.IncidentNeedResult();
			var need = await _incidentCommandService.SetNeedStatusAsync(DepartmentId, input.IncidentNeedId, (IncidentNeedStatus)input.Status, input.QuantityFulfilled, UserId, input.Note, CancellationToken.None);

			if (need == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = need;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Audit trail for one need: every fulfillment change with note, author, and timestamp (newest first).</summary>
		[HttpGet("GetNeedUpdates/{incidentNeedId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentNeedUpdatesResult>> GetNeedUpdates(string incidentNeedId)
		{
			var updates = await _incidentCommandService.GetNeedUpdatesAsync(DepartmentId, incidentNeedId);
			var result = new ICModels.IncidentNeedUpdatesResult { Data = updates, PageSize = updates.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Creates an Entity need requesting specific units/users/roles/groups; they are added to the call
		/// and dispatched individually as "Requested by Incident Command".
		/// </summary>
		[HttpPost("RequestNeedEntities")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageObjectives)]
		public async Task<ActionResult<ICModels.IncidentNeedResult>> RequestNeedEntities([FromBody] ICModels.RequestNeedEntitiesInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId) || string.IsNullOrWhiteSpace(input.Name) || input.Entities == null || input.Entities.Count == 0)
				return BadRequest();

			if (input.Entities.Any(e => !Enum.IsDefined(typeof(NeedEntityKind), e.EntityKind)))
				return BadRequest();

			try
			{
				var entities = input.Entities.Select(e => new IncidentNeedEntity { EntityKind = e.EntityKind, EntityId = e.EntityId }).ToList();
				var need = await _incidentCommandService.RequestNeedEntitiesAsync(DepartmentId, input.IncidentCommandId, input.Name, input.Description, entities, UserId, CancellationToken.None);
				var result = new ICModels.IncidentNeedResult
				{
					Data = need,
					PageSize = need == null ? 0 : 1,
					Status = need == null ? ResponseHelper.NotFound : ResponseHelper.Created
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (ArgumentException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		/// <summary>The requested entities under one Entity-category need.</summary>
		[HttpGet("GetNeedEntities/{incidentNeedId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentNeedEntitiesResult>> GetNeedEntities(string incidentNeedId)
		{
			var items = await _incidentCommandService.GetNeedEntitiesAsync(DepartmentId, incidentNeedId);
			var result = new ICModels.IncidentNeedEntitiesResult { Data = items, PageSize = items.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the command-level needs for a call.</summary>
		[HttpGet("GetNeeds/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentNeedsResult>> GetNeeds(int callId)
		{
			var result = new ICModels.IncidentNeedsResult();
			result.Data = await _incidentCommandService.GetNeedsForCallAsync(DepartmentId, callId);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Needs

		#region Timers

		/// <summary>Starts a scene/benchmark/role timer.</summary>
		[HttpPost("StartTimer")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageTimers)]
		public async Task<ActionResult<ICModels.IncidentTimerResult>> StartTimer([FromBody] IncidentTimer timer)
		{
			if (timer == null || string.IsNullOrWhiteSpace(timer.IncidentCommandId))
				return BadRequest();

			timer.DepartmentId = DepartmentId;

			var result = new ICModels.IncidentTimerResult();
			var saved = await _incidentCommandService.StartTimerAsync(timer, UserId, CancellationToken.None);

			if (saved == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Acknowledges a timer (resets its next-due time).</summary>
		[HttpPost("AcknowledgeTimer/{incidentTimerId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentTimerResult>> AcknowledgeTimer(string incidentTimerId)
		{
			var result = new ICModels.IncidentTimerResult();
			var timer = await _incidentCommandService.AcknowledgeTimerAsync(DepartmentId, incidentTimerId, UserId, CancellationToken.None);

			if (timer == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = timer;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Timers

		#region Map annotations

		/// <summary>Creates or updates a real-time map annotation.</summary>
		[HttpPost("SaveAnnotation")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageAnnotations)]
		public async Task<ActionResult<ICModels.IncidentMapAnnotationResult>> SaveAnnotation([FromBody] IncidentMapAnnotation annotation)
		{
			if (annotation == null || string.IsNullOrWhiteSpace(annotation.IncidentCommandId))
				return BadRequest();

			annotation.DepartmentId = DepartmentId;

			var result = new ICModels.IncidentMapAnnotationResult();
			var saved = await _incidentCommandService.SaveAnnotationAsync(annotation, UserId, CancellationToken.None);

			if (saved == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = saved;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Removes a map annotation.</summary>
		[HttpDelete("DeleteAnnotation/{incidentMapAnnotationId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> DeleteAnnotation(string incidentMapAnnotationId)
		{
			var result = new ICModels.IncidentCommandActionResult();
			result.Data = await _incidentCommandService.DeleteAnnotationAsync(DepartmentId, incidentMapAnnotationId, UserId, CancellationToken.None);
			result.Status = result.Data ? ResponseHelper.Success : ResponseHelper.NotFound;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Map annotations

		/// <summary>
		/// Creates or updates a NAMED incident map (name, description, framing, optional expiry). Audit
		/// fields stamp server-side; the change logs to the incident timeline with author + ICS standing.
		/// </summary>
		[HttpPost("SaveIncidentMap")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageAnnotations)]
		public async Task<ActionResult<ICModels.IncidentMapResult>> SaveIncidentMap([FromBody] IncidentMap map)
		{
			if (map == null || string.IsNullOrWhiteSpace(map.IncidentCommandId) || string.IsNullOrWhiteSpace(map.Name))
				return BadRequest();

			map.DepartmentId = DepartmentId;

			try
			{
				var saved = await _incidentCommandService.SaveIncidentMapAsync(map, UserId, CancellationToken.None);
				var result = new ICModels.IncidentMapResult
				{
					Data = saved,
					PageSize = saved == null ? 0 : 1,
					Status = saved == null ? ResponseHelper.NotFound : ResponseHelper.Success
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (ArgumentException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		/// <summary>Soft-deletes a named incident map.</summary>
		[HttpDelete("DeleteIncidentMap/{incidentMapId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		public async Task<ActionResult<ICModels.IncidentCommandActionResult>> DeleteIncidentMap(string incidentMapId)
		{
			var deleted = await _incidentCommandService.DeleteIncidentMapAsync(DepartmentId, incidentMapId, UserId, CancellationToken.None);
			var result = new ICModels.IncidentCommandActionResult { Data = deleted, Status = deleted ? ResponseHelper.Success : ResponseHelper.NotFound };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Gets the incident's named tactical maps.</summary>
		[HttpGet("GetIncidentMaps/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.IncidentMapsResult>> GetIncidentMaps(int callId)
		{
			var maps = await _incidentCommandService.GetIncidentMapsForCallAsync(DepartmentId, callId);
			var result = new ICModels.IncidentMapsResult { Data = maps, PageSize = maps.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Creates or updates the incident map's saved view (center + zoom). Logged to the incident
		/// timeline with the author's name and ICS standing.
		/// </summary>
		[HttpPut("UpdateMapView")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_Update)]
		[RequiresIncidentCapability(IncidentCapabilities.ManageAnnotations)]
		public async Task<ActionResult<ICModels.IncidentCommandResult>> UpdateMapView([FromBody] ICModels.UpdateMapViewInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.IncidentCommandId))
				return BadRequest();

			try
			{
				var command = await _incidentCommandService.UpdateMapViewAsync(DepartmentId, input.IncidentCommandId, input.CenterLatitude, input.CenterLongitude, input.ZoomLevel, UserId, CancellationToken.None);
				var result = new ICModels.IncidentCommandResult
				{
					Data = command,
					PageSize = command == null ? 0 : 1,
					Status = command == null ? ResponseHelper.NotFound : ResponseHelper.Success
				};
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (ArgumentException ex)
			{
				return BadRequest(ex.Message);
			}
		}

		#region Timeline

		/// <summary>Gets the append-only command (ICS-201) timeline for a call.</summary>
		[HttpGet("GetTimeline/{callId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<ICModels.CommandTimelineResult>> GetTimeline(int callId)
		{
			var result = new ICModels.CommandTimelineResult();
			result.Data = await _incidentCommandService.GetTimelineForCallAsync(DepartmentId, callId);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Timeline

		private async Task<bool> HasCapabilityAsync(string incidentCommandId, IncidentCapabilities required)
		{
			var command = await _incidentCommandService.GetCommandByIdAsync(incidentCommandId);
			if (command == null || command.DepartmentId != DepartmentId)
				return false;

			var capabilities = await _incidentCommandService.GetCapabilitiesForUserAsync(DepartmentId, command.CallId, UserId);
			return (capabilities & required) == required;
		}
	}
}
