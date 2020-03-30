using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Model;
using Resgrid.Web.Services.Controllers.Version3.Models.Notes;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against a departments notes
	/// </summary>
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
		[AcceptVerbs("GET")]
		public List<NotesResult> GetAllNotes()
		{
			var results = new List<NotesResult>();
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			var notes = _notesService.GetNotesForDepartmentFiltered(DepartmentId, department.IsUserAnAdmin(UserId));

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

			return results;
		}

		/// <summary>
		/// Get's all the notes in a department
		/// </summary>
		/// <returns>List of NotesResult objects.</returns>
		[AcceptVerbs("GET")]
		public List<NotesResult> GetAllUnexpiredNotesByCategory(string category, bool includeUnCategorized)
		{
			var results = new List<NotesResult>();
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			var notes = _notesService.GetNotesForDepartmentFiltered(DepartmentId, department.IsUserAnAdmin(UserId));

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

			return results;
		}

		/// <summary>
		/// Gets a specific note by it's Id.
		/// </summary>
		/// <param name="noteId">Integer note Identifier</param>
		/// <returns>NotesResult object populated with note information from the system.</returns>
		[AcceptVerbs("GET")]
		public NotesResult GetNote(int noteId)
		{
			var note = _notesService.GetNoteById(noteId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			if (note != null)
			{
				if (note.DepartmentId != DepartmentId)
					throw HttpStatusCode.Unauthorized.AsException();

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

				return noteResult;
			}

			return null;
		}

		/// <summary>
		/// Get's all the notes in a department
		/// </summary>
		/// <returns>List of NotesResult objects.</returns>
		[AcceptVerbs("POST")]
		public HttpResponseMessage SaveNote(NewNoteInput input)
		{
			var result = new HttpResponseMessage(HttpStatusCode.OK);

			var note = new Note();
			note.DepartmentId = DepartmentId;
			note.AddedOn = DateTime.UtcNow;
			note.IsAdminOnly = input.Ado;
			note.Category = input.Cat;
			note.Title = input.Ttl;
			note.Body = input.Bdy;
			note.UserId = UserId;

			_notesService.Save(note);

			return result;
		}

		/// <summary>
		/// Get's all the note Categories for a Department
		/// </summary>
		/// <returns>List of string of distinct note category.</returns>
		[AcceptVerbs("GET")]
		public List<string> GetNoteCategories()
		{
			var result = _notesService.GetDistinctCategoriesByDepartmentId(DepartmentId);

			return result;
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}
