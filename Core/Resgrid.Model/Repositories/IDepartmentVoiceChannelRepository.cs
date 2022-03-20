using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentVoiceChannelRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentVoiceChannel}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentVoiceChannel}" />
	public interface IDepartmentVoiceChannelRepository : IRepository<DepartmentVoiceChannel>
	{
		Task<IEnumerable<DepartmentVoiceChannel>> GetDepartmentVoiceChannelByVoiceIdAsync(string voiceId);

		Task<IEnumerable<DepartmentVoiceChannel>> GetDepartmentVoiceChannelByDepartmentIdAsync(int departmentId);
	}
}
