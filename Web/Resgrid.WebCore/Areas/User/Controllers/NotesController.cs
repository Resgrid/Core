using System;
using Microsoft.AspNetCore.Mvc;
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

		public IActionResult Index()
		{
			IndexView model = new IndexView();
			model.Notes = _notesService.GetAllNotesForDepartment(DepartmentId);

			return View(model);
		}

		[HttpGet]
		public IActionResult NewNote()
		{
			NewNoteView model = new NewNoteView();

			return View(model);
		}

		[HttpPost]
		public IActionResult NewNote(NewNoteView model)
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

				_notesService.Save(note);

				return RedirectToAction("Index", "Notes", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		public IActionResult View(int noteId)
		{
			ViewNoteView model = new ViewNoteView();

			var note = _notesService.GetNoteById(noteId);

			if (note.DepartmentId != DepartmentId)
				Unauthorized();

			model.Note = note;
			model.Department = _departmentsService.GetDepartmentById(note.DepartmentId);

			return View(model);
		}

		[HttpGet]
		public IActionResult Edit(int noteId)
		{
			EditNoteView model = new EditNoteView();

			var note = _notesService.GetNoteById(noteId);

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
		public IActionResult Edit(EditNoteView model)
		{
			if (ModelState.IsValid)
			{
				var savedNote = _notesService.GetNoteById(model.NoteId);

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

				_notesService.Save(savedNote);

				return RedirectToAction("Index", "Notes", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		public IActionResult Delete(int noteId)
		{
			var note = _notesService.GetNoteById(noteId);

			if (note.DepartmentId != DepartmentId)
				Unauthorized();

			_notesService.Delete(note);

			return RedirectToAction("Index", "Notes", new { Area = "User" });
		}

		[HttpGet]
		public IActionResult GetDepartmentNotesCategories()
		{
			return Json(_notesService.GetDistinctCategoriesByDepartmentId(DepartmentId));
		}
	}
}