using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IFirebaseAuthProvider
	{
		Task<string> CreateResponderCustomAuthToken(string userId, Dictionary<string, object> claims = null);
	}
}
