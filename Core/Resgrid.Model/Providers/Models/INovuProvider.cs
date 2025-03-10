using System.Threading.Tasks;

namespace Resgrid.Model.Providers;

public interface INovuProvider
{
	Task<bool> CreateSubscriber(string userId, int departmentId, string email, string firstName, string lastName);
}
