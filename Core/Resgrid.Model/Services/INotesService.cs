using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface INotesService
	{
		/// <summary>
		/// Gets all notes for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Note&gt;&gt;.</returns>
		Task<List<Note>> GetAllNotesForDepartmentAsync(int departmentId);

		/// <summary>
		/// Saves the asynchronous.
		/// </summary>
		/// <param name="note">The note.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Note&gt;.</returns>
		Task<Note> SaveAsync(Note note, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the distinct categories by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;System.String&gt;&gt;.</returns>
		Task<List<string>> GetDistinctCategoriesByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the note by identifier asynchronous.
		/// </summary>
		/// <param name="noteId">The note identifier.</param>
		/// <returns>Task&lt;Note&gt;.</returns>
		Task<Note> GetNoteByIdAsync(int noteId);

		/// <summary>
		/// Deletes the asynchronous.
		/// </summary>
		/// <param name="note">The note.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteAsync(Note note, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the notes for department filtered asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="isAdmin">if set to <c>true</c> [is admin].</param>
		/// <returns>Task&lt;List&lt;Note&gt;&gt;.</returns>
		Task<List<Note>> GetNotesForDepartmentFilteredAsync(int departmentId, bool isAdmin);

		Task<NoteCategory> SaveNoteCategoryAsync(NoteCategory category, CancellationToken cancellationToken = default(CancellationToken));

		Task<NoteCategory> GetNoteCategoryByIdAsync(string categoryId);

		Task<bool> DeleteNoteCategoryAsync(NoteCategory category, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<NoteCategory>> GetAllCategoriesByDepartmentIdAsync(int departmentId);

		Task<bool> DoesNoteTypeAlreadyExistAsync(int departmentId, string noteTypeText);
	}
}
