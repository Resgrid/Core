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

namespace Resgrid.Services
{
	public class NotesService : INotesService
	{
		private readonly INotesRepository _notesRepository;
		private readonly IEventAggregator _eventAggregator;

		public NotesService(INotesRepository notesRepository, IEventAggregator eventAggregator)
		{
			_notesRepository = notesRepository;
			_eventAggregator = eventAggregator;
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
	}
}
