using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IHealthRepository
	{
		Task<string> GetDatabaseCurrentTime();
	}
}
