using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Services;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Web.Services.Controllers.Version3.Models.Notes;


namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against a departments notes
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class NotesController : V3AuthenticatedApiControllerbase
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly INotesService _notesService;

		public NotesController(IDepartmentsService departmentsService, INotesService notesService)
		{
			_departmentsService = departmentsService;
			_notesService = notesService;
		}

		/// <summary>
		/// Get's all the notes in a department
		/// </summary>
		/// <returns>List of NotesResult objects.</returns>
		[HttpGet("GetAllNotes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<NotesResult>>> GetAllNotes()
		{
			var results = new List<NotesResult>();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			var notes = await _notesService.GetNotesForDepartmentFilteredAsync(DepartmentId, department.IsUserAnAdmin(UserId));

			foreach (var n in notes)
			{
				var noteResult = new NotesResult();
				noteResult.Nid = n.NoteId;
				noteResult.Uid = n.UserId;
				noteResult.Ttl = n.Title;
				noteResult.Bdy = StringHelpers.StripHtmlTagsCharArray(n.Body).Truncate(100);
				noteResult.Adn = n.AddedOn.TimeConverter(department);
				noteResult.Cat = n.Category;
				noteResult.Clr = n.Color;

				if (n.ExpiresOn.HasValue)
					noteResult.Exp = n.ExpiresOn.Value;

				results.Add(noteResult);
			}

			return Ok(results);
		}

		/// <summary>
		/// Get's all the notes in a department
		/// </summary>
		/// <returns>List of NotesResult objects.</returns>
		[HttpGet("GetAllUnexpiredNotesByCategory")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<NotesResult>>> GetAllUnexpiredNotesByCategory(string category, bool includeUnCategorized)
		{
			var results = new List<NotesResult>();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			var notes = await _notesService.GetNotesForDepartmentFilteredAsync(DepartmentId, department.IsUserAnAdmin(UserId));

			foreach (var n in notes)
			{
				if ((!n.StartsOn.HasValue || n.StartsOn.Value >= DateTime.UtcNow) && (!n.ExpiresOn.HasValue || n.ExpiresOn.Value <= DateTime.UtcNow))
				{
					if (!String.IsNullOrWhiteSpace(category) && !String.IsNullOrWhiteSpace(n.Category) && n.Category.Trim().Equals(category, StringComparison.InvariantCultureIgnoreCase))
					{
						var noteResult = new NotesResult();
						noteResult.Nid = n.NoteId;
						noteResult.Uid = n.UserId;
						noteResult.Ttl = n.Title;
						noteResult.Bdy = StringHelpers.StripHtmlTagsCharArray(n.Body).Truncate(100);
						noteResult.Adn = n.AddedOn.TimeConverter(department);
						noteResult.Cat = n.Category;
						noteResult.Clr = n.Color;

						if (n.ExpiresOn.HasValue)
							noteResult.Exp = n.ExpiresOn.Value;

						results.Add(noteResult);
					}
					else if (includeUnCategorized && String.IsNullOrWhiteSpace(n.Category))
					{
						var noteResult = new NotesResult();
						noteResult.Nid = n.NoteId;
						noteResult.Uid = n.UserId;
						noteResult.Ttl = n.Title;
						noteResult.Bdy = StringHelpers.StripHtmlTagsCharArray(n.Body).Truncate(100);
						noteResult.Adn = n.AddedOn.TimeConverter(department);
						noteResult.Cat = n.Category;
						noteResult.Clr = n.Color;

						if (n.ExpiresOn.HasValue)
							noteResult.Exp = n.ExpiresOn.Value;

						results.Add(noteResult);
					}
				}
			}

			return Ok(results);
		}

		/// <summary>
		/// Gets a specific note by it's Id.
		/// </summary>
		/// <param name="noteId">Integer note Identifier</param>
		/// <returns>NotesResult object populated with note information from the system.</returns>
		[HttpGet("GetNote")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<NotesResult>> GetNote(int noteId)
		{
			var note = await _notesService.GetNoteByIdAsync(noteId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (note != null)
			{
				if (note.DepartmentId != DepartmentId)
					return Unauthorized();

				var noteResult = new NotesResult();
				noteResult.Nid = note.NoteId;
				noteResult.Uid = note.UserId;
				noteResult.Ttl = note.Title;
				noteResult.Bdy = StringHelpers.StripHtmlTagsCharArray(note.Body).Truncate(100);
				noteResult.Adn = note.AddedOn.TimeConverter(department);
				noteResult.Cat = note.Category;
				noteResult.Clr = note.Color;

				if (note.ExpiresOn.HasValue)
					noteResult.Exp = note.ExpiresOn.Value;

				return Ok(noteResult);
			}

			return NotFound();
		}

		/// <summary>
		/// Get's all the notes in a department
		/// </summary>
		/// <returns>List of NotesResult objects.</returns>
		[HttpPost("SaveNote")]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> SaveNote(NewNoteInput input, CancellationToken cancellationToken)
		{
			var note = new Note();
			note.DepartmentId = DepartmentId;
			note.AddedOn = DateTime.UtcNow;
			note.IsAdminOnly = input.Ado;
			note.Category = input.Cat;
			note.Title = input.Ttl;
			note.Body = input.Bdy;
			note.UserId = UserId;

			var saved = await _notesService.SaveAsync(note, cancellationToken);

			return CreatedAtAction(nameof(SaveNote), new { id = saved.NoteId }, saved);
		}

		/// <summary>
		/// Get's all the note Categories for a Department
		/// </summary>
		/// <returns>List of string of distinct note category.</returns>
		[HttpGet("GetNoteCategories")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<string>>> GetNoteCategories()
		{
			var result = await _notesService.GetDistinctCategoriesByDepartmentIdAsync(DepartmentId);

			return result;
		}
	}
}
