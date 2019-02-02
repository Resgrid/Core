using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface INotesService
	{
		List<Note> GetAllNotesForDepartment(int departmentId);
		Note Save(Note note);
		List<string> GetDistinctCategoriesByDepartmentId(int departmentId);
		Note GetNoteById(int noteId);
		void Delete(Note note);
	    List<Note> GetNotesForDepartmentFiltered(int departmentId, bool isAdmin);
	}
}