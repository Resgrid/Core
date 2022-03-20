using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentVoiceUserRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentVoiceUser}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentVoiceUser}" />
	public interface IDepartmentVoiceUserRepository : IRepository<DepartmentVoiceUser>
	{
		Task<DepartmentVoiceUser> GetDepartmentVoiceUserByUserIdAsync(string userId);
	}
}
