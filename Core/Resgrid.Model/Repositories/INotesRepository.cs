using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface INotesRepository
	/// Implements the <see cref="Note" />
	/// </summary>
	/// <seealso cref="Note" />
	public interface INotesRepository: IRepository<Note>
	{
		/// <summary>
		/// Gets the notes by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Note&gt;&gt;.</returns>
		Task<IEnumerable<Note>> GetNotesByDepartmentIdAsync(int departmentId);
	}
}
