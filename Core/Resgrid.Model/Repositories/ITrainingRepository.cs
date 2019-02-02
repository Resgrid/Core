using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface ITrainingRepository : IRepository<Training>
	{
		List<Training> GetAllTrainings();
	}
}