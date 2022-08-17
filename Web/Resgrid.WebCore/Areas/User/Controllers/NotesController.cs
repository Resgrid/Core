using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Notes;
using Resgrid.Web.Helpers;
using IndexView = Resgrid.Web.Areas.User.Models.Notes.IndexView;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class NotesController : SecureBaseController
	{
		private readonly INotesService _notesService;
		private readonly IDepartmentsService _departmentsService;

		public NotesController(INotesService notesService, IDepartmentsService departmentsService)
		{
			_notesService = notesService;
			_departmentsService = departmentsService;
		}

		public async Task<IActionResult> Index()
		{
			IndexView model = new IndexView();
			model.Notes = await _notesService.GetAllNotesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> NewNote()
		{
			NewNoteView model = new NewNoteView();

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> NewNote(NewNoteView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				Note note = new Note();
				note.DepartmentId = DepartmentId;
				note.UserId = UserId;
				note.AddedOn = DateTime.UtcNow;
				note.Title = model.Title;
				note.Body = System.Net.WebUtility.HtmlDecode(model.Body);

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					note.IsAdminOnly = bool.Parse(model.IsAdminOnly);
				else
					note.IsAdminOnly = false;

				note.Category = model.Category;

				await _notesService.SaveAsync(note, cancellationToken);

				return RedirectToAction("Index", "Notes", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> View(int noteId)
		{
			ViewNoteView model = new ViewNoteView();

			var note = await _notesService.GetNoteByIdAsync(noteId);

			if (note.DepartmentId != DepartmentId)
				Unauthorized();

			model.Note = note;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(note.DepartmentId);

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Edit(int noteId)
		{
			EditNoteView model = new EditNoteView();

			var note = await _notesService.GetNoteByIdAsync(noteId);

			if (note.DepartmentId != DepartmentId)
				Unauthorized();

			model.NoteId = note.NoteId;
			model.Title = note.Title;
			model.Body = note.Body;
			model.IsAdminOnly = note.IsAdminOnly.ToString();

			model.Category = note.Category;

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> Edit(EditNoteView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var savedNote = await _notesService.GetNoteByIdAsync(model.NoteId);

				if (savedNote.DepartmentId != DepartmentId)
					Unauthorized();

				savedNote.DepartmentId = DepartmentId;
				savedNote.UserId = UserId;
				savedNote.AddedOn = DateTime.UtcNow;
				savedNote.Title = model.Title;
				savedNote.Body = System.Net.WebUtility.HtmlDecode(model.Body);

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					savedNote.IsAdminOnly = bool.Parse(model.IsAdminOnly);
				else
					savedNote.IsAdminOnly = false;

				savedNote.Category = model.Category;

				await _notesService.SaveAsync(savedNote, cancellationToken);

				return RedirectToAction("Index", "Notes", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Delete(int noteId, CancellationToken cancellationToken)
		{
			var note = await _notesService.GetNoteByIdAsync(noteId);

			if (note.DepartmentId != DepartmentId)
				Unauthorized();

			await _notesService.DeleteAsync(note, cancellationToken);

			return RedirectToAction("Index", "Notes", new { Area = "User" });
		}

		[HttpGet]
		public async Task<IActionResult> GetDepartmentNotesCategories()
		{
			return Json(_notesService.GetDistinctCategoriesByDepartmentIdAsync(DepartmentId));
		}
	}
}
