using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The personnel status table's grouping and sorting, shared by the classic dashboard (HomeController.GetUserStatusTable)
	/// and the home dashboard's personnel.board widget.
	/// </summary>
	public interface IPersonnelStatusBoardService
	{
		/// <summary>
		/// The department's people grouped as the status table shows them, filtered to those <paramref name="viewerUserId"/>
		/// may see through the personnel visibility matrix; disabled and hidden members are never listed.
		/// </summary>
		Task<PersonnelStatusBoard> GetBoardAsync(int departmentId, string viewerUserId);
	}
}
