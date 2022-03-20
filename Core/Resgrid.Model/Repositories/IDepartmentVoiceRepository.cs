using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentVoiceRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentVoice}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentVoice}" />
	public interface IDepartmentVoiceRepository : IRepository<DepartmentVoice>
	{
		Task<DepartmentVoice> GetDepartmentVoiceByDepartmentIdAsync(int departmentId);
	}
}
