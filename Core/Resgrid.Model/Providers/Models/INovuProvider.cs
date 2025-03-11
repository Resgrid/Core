using System.Threading.Tasks;

namespace Resgrid.Model.Providers;

public interface INovuProvider
{
	Task<bool> CreateUserSubscriber(string userId, string code, int departmentId, string email, string firstName, string lastName);
	Task<bool> CreateUnitSubscriber(int unitId, string code, int departmentId, string unitName);
}
