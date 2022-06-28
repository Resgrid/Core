using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model;
using System.Collections.Generic;
using Resgrid.Web.Services.Models.v4.Notes;
using Resgrid.Model.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// The options for Notes in the Resgrid system
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class NotesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly INotesService _notesService;

		public NotesController(IDepartmentsService departmentsService, INotesService notesService)
		{
			_departmentsService = departmentsService;
			_notesService = notesService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all notes for a department
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllNotes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Notes_View)]
		public async Task<ActionResult<GetAllNotesResult>> GetAllNotes()
		{
			var result = new GetAllNotesResult();
			result.Data = new List<NotesResultData>();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var notes = await _notesService.GetNotesForDepartmentFilteredAsync(DepartmentId, department.IsUserAnAdmin(UserId));

			if (notes != null && notes.Any())
			{

				foreach (var n in notes)
				{
					result.Data.Add(ConvertNoteData(n, department));
				}

				result.Data = result.Data.OrderBy(x => x.AddedOn).ToList();

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets the dispatch note (if any). A dispatch note is a Department note with the
		/// title of "Dispatch" or a note in a Category of "Dispatch".
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetDispatchNote")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Notes_View)]
		public async Task<ActionResult<GetNoteResult>> GetDispatchNote()
		{
			var result = new GetNoteResult();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var notes = await _notesService.GetNotesForDepartmentFilteredAsync(DepartmentId, false);

			if (notes != null && notes.Any())
			{
				var explicitDispatchNote = notes.FirstOrDefault(x => x.Title == "Dispatch");
				var dispatchCategoryNote = notes.FirstOrDefault(x => x.Category == "Dispatch");

				if (explicitDispatchNote != null && (!explicitDispatchNote.ExpiresOn.HasValue || explicitDispatchNote.ExpiresOn.Value >= System.DateTime.UtcNow))
				{
					result.Data = ConvertNoteData(explicitDispatchNote, department);

					result.PageSize = 1;
					result.Status = ResponseHelper.Success;
				}
				else if (dispatchCategoryNote != null && (!explicitDispatchNote.ExpiresOn.HasValue || explicitDispatchNote.ExpiresOn.Value >= System.DateTime.UtcNow))
				{
					result.Data = ConvertNoteData(dispatchCategoryNote, department);

					result.PageSize = 1;
					result.Status = ResponseHelper.Success;
				}
				else
				{
					result.PageSize = 0;
					result.Status = ResponseHelper.NotFound;
				}
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		public static NotesResultData ConvertNoteData(Note note, Department department)
		{
			var noteResult = new NotesResultData();
			noteResult.NoteId = note.NoteId.ToString();
			noteResult.UserId = note.UserId;
			noteResult.Title = note.Title;
			noteResult.Body = note.Body;
			noteResult.AddedOn = note.AddedOn.TimeConverter(department);
			noteResult.Category = note.Category;
			noteResult.Color = note.Color;

			if (note.ExpiresOn.HasValue)
				noteResult.ExpiresOn = note.ExpiresOn.Value.TimeConverter(department);;

			return noteResult;
		}
	}
}
