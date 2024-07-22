using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Repositories.DataRepository;

namespace Resgrid.Services
{
	public class NotesService : INotesService
	{
		private readonly INotesRepository _notesRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly INoteCategoriesRepository _noteCategoriesRepository;

		public NotesService(INotesRepository notesRepository, IEventAggregator eventAggregator, INoteCategoriesRepository noteCategoriesRepository)
		{
			_notesRepository = notesRepository;
			_eventAggregator = eventAggregator;
			_noteCategoriesRepository = noteCategoriesRepository;
		}

		public async Task<List<Note>> GetAllNotesForDepartmentAsync(int departmentId)
		{
			var notes = await _notesRepository.GetNotesByDepartmentIdAsync(departmentId);

			// Temp fix for the Notes screen in the Core web app and it's sticky notes display. -SJ
			foreach (var note in notes)
			{
				note.Body = StringHelpers.SanitizeHtmlInString(note.Body);
			}

			return notes.ToList();
		}

		public async Task<Note> SaveAsync(Note note, CancellationToken cancellationToken = default(CancellationToken))
		{
			note.Body = StringHelpers.SanitizeHtmlInString(note.Body);
			var saved = await _notesRepository.SaveOrUpdateAsync(note, cancellationToken);
			_eventAggregator.SendMessage<NoteAddedEvent>(new NoteAddedEvent() { DepartmentId = note.DepartmentId, Note = note });

			return saved;
		}

		public async Task<List<string>> GetDistinctCategoriesByDepartmentIdAsync(int departmentId)
		{
			var allCategories = await _notesRepository.GetAllByDepartmentIdAsync(departmentId);
			var categories = (from doc in allCategories
							  where !string.IsNullOrEmpty(doc.Category)
							  select doc.Category).Distinct().ToList();

			return categories;
		}

		public async Task<Note> GetNoteByIdAsync(int noteId)
		{
			return await _notesRepository.GetByIdAsync(noteId);
		}

		public async Task<bool> DeleteAsync(Note note, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _notesRepository.DeleteAsync(note, cancellationToken);
		}

		public async Task<List<Note>> GetNotesForDepartmentFilteredAsync(int departmentId, bool isAdmin)
		{
			var notes = await _notesRepository.GetAllByDepartmentIdAsync(departmentId);

			if (isAdmin)
			{
				return notes.ToList();
			}

			return (from note in notes
					where note.IsAdminOnly == false
					select note).ToList();
		}

		public async Task<NoteCategory> SaveNoteCategoryAsync(NoteCategory category, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _noteCategoriesRepository.SaveOrUpdateAsync(category, cancellationToken);
		}

		public async Task<NoteCategory> GetNoteCategoryByIdAsync(string categoryId)
		{
			return await _noteCategoriesRepository.GetByIdAsync(categoryId);
		}

		public async Task<bool> DeleteNoteCategoryAsync(NoteCategory category, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _noteCategoriesRepository.DeleteAsync(category, cancellationToken);
		}

		public async Task<List<NoteCategory>> GetAllCategoriesByDepartmentIdAsync(int departmentId)
		{
			var categories = await _noteCategoriesRepository.GetAllByDepartmentIdAsync(departmentId);

			if (categories != null)
				return categories.ToList();

			return new List<NoteCategory>();
		}

		public async Task<bool> DoesNoteTypeAlreadyExistAsync(int departmentId, string noteTypeText)
		{
			var categories = await _noteCategoriesRepository.GetAllByDepartmentIdAsync(departmentId);

			if (categories == null)
				return false;

			return categories.Any(x => x.Name == noteTypeText.Trim());
		}
	}
}
