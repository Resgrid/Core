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

		public CallNotesController(ICallsService callsService, IDepartmentsService departmentsService)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
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

			if (call.CallNotes != null && call.CallNotes.Any())
			{
				foreach (var note in call.CallNotes)
				{

					var fullName = await UserHelper.GetFullNameForUser(note.UserId);

					result.Data.Add(ConvertCallNote(note, fullName, department));
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

			var saved = await _callsService.SaveCallNoteAsync(note, cancellationToken);

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
