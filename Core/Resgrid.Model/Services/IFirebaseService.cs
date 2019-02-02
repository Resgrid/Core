using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IFirebaseService
	{
		string CreateToken(string uid, Dictionary<string, object> claims = null);
	}
}
