using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using System.Linq;
using Resgrid.Model;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Models.v4.CallNotes;
using System;
using Resgrid.Model.Helpers;
using static Resgrid.Web.Services.Models.v4.CallNotes.CallNotesResult;
using System.Net.Mime;
using System.Threading;
using Resgrid.Web.Services.Models.v4.Calls;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CallNotesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IProtectedReadService _protectedCallReadService;
		private readonly IProtectedWriteService _protectedWriteService;

		public CallNotesController(ICallsService callsService, IDepartmentsService departmentsService,
			IProtectedReadService protectedCallReadService, IProtectedWriteService protectedWriteService)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
			_protectedCallReadService = protectedCallReadService;
			_protectedWriteService = protectedWriteService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Get notes for a call
		/// </summary>
		/// <param name="callId">CallId of the call you want to get notes for</param>
		/// <returns></returns>
		[HttpGet("GetCallNotes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CallNotesResult>> GetCallNotes(string callId)
		{
			if (String.IsNullOrWhiteSpace(callId))
				return BadRequest();

			var result = new CallNotesResult();

			var call = await _callsService.GetCallByIdAsync(int.Parse(callId));
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			call = await _callsService.PopulateCallData(call, false, false, true, false, false, false, false, false, false);

			// Attended protected read (plan 7.1): note text and companion coordinates decrypt with a
			// valid grant or read as REDACTED/null — never an envelope.
			var protectedRead = await _protectedCallReadService.ResolveNotesForReadAsync(DepartmentId,
				call.CallNotes?.ToList(), Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId);

			if (call.CallNotes != null && call.CallNotes.Any())
			{
				foreach (var note in call.CallNotes)
				{

					var fullName = await UserHelper.GetFullNameForUser(note.UserId);

					var noteData = ConvertCallNote(note, fullName, department);
					noteData.IsProtected = protectedRead.IsProtected;
					noteData.ProtectedReason = protectedRead.ProtectedReason;
					result.Data.Add(noteData);
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Saves a call note
		/// </summary>
		/// <param name="input">CallId of the call you want to get notes for</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>ActionResult.</returns>
		[HttpPost("SaveCallNote")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<SaveCallNoteResult>> SaveCallNote(SaveCallNoteInput input, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(int.Parse(input.CallId));

			if (call == null)
				return BadRequest();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			var result = new SaveCallNoteResult();

			var note = new CallNote();
			note.CallId = int.Parse(input.CallId);
			note.Timestamp = DateTime.UtcNow;
			note.Note = input.Note;
			note.UserId = input.UserId;
			note.Source = (int)CallNoteSources.Mobile;

			if (!String.IsNullOrWhiteSpace(input.Latitude) && !String.IsNullOrWhiteSpace(input.Longitude))
			{
				note.Latitude = decimal.Parse(input.Latitude);
				note.Longitude = decimal.Parse(input.Longitude);
			}

			// ADP write preflight (plan 3.3): refuse BEFORE inserting the transient plaintext row.
			var writePreflight = await _protectedWriteService.PreflightWriteAsync(DepartmentId,
				Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId, workloadCaller: false, cancellationToken);
			if (!writePreflight.Success)
				return Problem(type: writePreflight.Reason,
					title: "Recent multi-factor verification is required to modify protected data.",
					statusCode: StatusCodes.Status403Forbidden);

			var saved = await _callsService.SaveCallNoteAsync(note, cancellationToken);

			// ADP two-phase write (plan 19.2): the identity pk is an AAD component — encrypt now
			// that the id exists, then persist the enveloped row (companion coordinates move into
			// their envelope columns).
			var protectedWrite = await _protectedWriteService.PrepareCallNoteWriteAsync(DepartmentId, saved,
				Request.Headers[DataProtectionController.GrantHeader].ToString(), UserId, workloadCaller: false, cancellationToken);
			if (!protectedWrite.Success)
			{
				Resgrid.Framework.Logging.LogError($"ADP protected write failed AFTER insert for call note {saved.CallNoteId} in department {DepartmentId} ({protectedWrite.Reason}); transient plaintext row pending re-encryption.");
				return Problem(type: protectedWrite.Reason,
					title: "The note was saved but could not be protected; protected storage is temporarily unavailable. Do not resubmit — the note will be encrypted automatically.",
					statusCode: StatusCodes.Status503ServiceUnavailable);
			}
			if (protectedWrite.IsProtected)
				saved = await _callsService.SaveCallNoteAsync(saved, cancellationToken);

			result.Id = saved.CallNoteId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);

			return CreatedAtAction(nameof(GetCallNotes), new { callId = saved.CallId }, result);
		}

		public static CallNoteResultData ConvertCallNote(CallNote note, string fullName, Department department)
		{
			var noteResult = new CallNoteResultData();
			noteResult.CallNoteId = note.CallNoteId.ToString();
			noteResult.CallId = note.CallId.ToString();
			noteResult.Source = note.Source;
			noteResult.UserId = note.UserId;
			noteResult.TimestampFormatted = note.Timestamp.TimeConverter(department).FormatForDepartment(department);
			noteResult.TimestampUtc = note.Timestamp;
			noteResult.Note = note.Note;
			noteResult.Latitude = note.Latitude;
			noteResult.Longitude = note.Longitude;
			noteResult.FullName = fullName;

			return noteResult;
		}
	}
}
