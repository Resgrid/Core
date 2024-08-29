using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Notes;
using Resgrid.Web.Helpers;
using AuditEvent = Resgrid.Model.Events.AuditEvent;
using IndexView = Resgrid.Web.Areas.User.Models.Notes.IndexView;
using Note = Resgrid.Model.Note;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class NotesController : SecureBaseController
	{
		private readonly INotesService _notesService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IEventAggregator _eventAggregator;

		public NotesController(INotesService notesService, IDepartmentsService departmentsService, IAuthorizationService authorizationService, IEventAggregator eventAggregator)
		{
			_notesService = notesService;
			_departmentsService = departmentsService;
			_authorizationService = authorizationService;
			_eventAggregator = eventAggregator;
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
			if (!await _authorizationService.CanUserAddNoteAsync(UserId))
				Unauthorized();

			NewNoteView model = new NewNoteView();

			List<NoteCategory> noteCategories = new List<NoteCategory>();
			noteCategories.Add(new NoteCategory { Name = "None" });
			noteCategories.AddRange(await _notesService.GetAllCategoriesByDepartmentIdAsync(DepartmentId));
			model.Categories = new SelectList(noteCategories, "Name", "Name");

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> NewNote(NewNoteView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserAddNoteAsync(UserId))
				Unauthorized();

			if (ModelState.IsValid)
			{
				Note note = new Note();
				note.DepartmentId = DepartmentId;
				note.UserId = UserId;
				note.AddedOn = DateTime.UtcNow;
				note.Title = model.Title;
				note.Body = System.Net.WebUtility.HtmlDecode(model.Body);

				if (!String.IsNullOrWhiteSpace(model.Category) && model.Category != "None")
					note.Category = model.Category;

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					note.IsAdminOnly = bool.Parse(model.IsAdminOnly);
				else
					note.IsAdminOnly = false;

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = note.CloneJsonToString();
				auditEvent.Type = AuditLogTypes.NoteAdded;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);


				await _notesService.SaveAsync(note, cancellationToken);

				return RedirectToAction("Index", "Notes", new { Area = "User" });
			}

			List<NoteCategory> noteCategories = new List<NoteCategory>();
			noteCategories.Add(new NoteCategory { Name = "None" });
			noteCategories.AddRange(await _notesService.GetAllCategoriesByDepartmentIdAsync(DepartmentId));
			model.Categories = new SelectList(noteCategories, "Name", "Name");

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

			if (!await _authorizationService.CanUserEditNoteAsync(UserId, noteId))
				Unauthorized();

			List<NoteCategory> noteCategories = new List<NoteCategory>();
			noteCategories.Add(new NoteCategory { Name = "None" });
			noteCategories.AddRange(await _notesService.GetAllCategoriesByDepartmentIdAsync(DepartmentId));
			model.Categories = new SelectList(noteCategories, "Name", "Name");

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

				var auditEvent = new AuditEvent();
				auditEvent.Before = savedNote.CloneJsonToString();

				savedNote.DepartmentId = DepartmentId;
				savedNote.UserId = UserId;
				savedNote.AddedOn = DateTime.UtcNow;
				savedNote.Title = model.Title;
				savedNote.Body = System.Net.WebUtility.HtmlDecode(model.Body);

				if (!String.IsNullOrWhiteSpace(model.Category) && model.Category != "None")
					savedNote.Category = model.Category;
				else
					savedNote.Category = null;

				if (ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					savedNote.IsAdminOnly = bool.Parse(model.IsAdminOnly);
				else
					savedNote.IsAdminOnly = false;

				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.After = savedNote.CloneJsonToString();
				auditEvent.Type = AuditLogTypes.NoteEdited;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				await _notesService.SaveAsync(savedNote, cancellationToken);

				return RedirectToAction("Index", "Notes", new { Area = "User" });
			}

			List<NoteCategory> noteCategories = new List<NoteCategory>();
			noteCategories.Add(new NoteCategory { Name = "None" });
			noteCategories.AddRange(await _notesService.GetAllCategoriesByDepartmentIdAsync(DepartmentId));
			model.Categories = new SelectList(noteCategories, "Name", "Name");

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Delete(int noteId, CancellationToken cancellationToken)
		{
			var note = await _notesService.GetNoteByIdAsync(noteId);

			if (!await _authorizationService.CanUserEditNoteAsync(UserId, noteId))
				Unauthorized();

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Before = note.CloneJsonToString();
			auditEvent.Type = AuditLogTypes.NoteRemoved;
			auditEvent.Successful = true;
			auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
			auditEvent.ServerName = Environment.MachineName;
			auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _notesService.DeleteAsync(note, cancellationToken);

			return RedirectToAction("Index", "Notes", new { Area = "User" });
		}

		[HttpGet]
		public async Task<IActionResult> GetDepartmentNotesCategories()
		{
			return Json(await _notesService.GetDistinctCategoriesByDepartmentIdAsync(DepartmentId));
		}
	}
}
