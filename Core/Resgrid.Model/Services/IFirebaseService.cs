using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IFirebaseService
	{
		Task<string> CreateTokenAsync(string uid, Dictionary<string, object> claims = null);
	}
}
