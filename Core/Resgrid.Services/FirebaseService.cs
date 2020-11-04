using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class FirebaseService: IFirebaseService
	{
		private readonly IFirebaseAuthProvider _firebaseAuthProvider;

		public FirebaseService(IFirebaseAuthProvider firebaseAuthProvider)
		{
			_firebaseAuthProvider = firebaseAuthProvider;
		}

		public async Task<string> CreateTokenAsync(string uid, Dictionary<string, object> claims = null)
		{
			return await _firebaseAuthProvider.CreateResponderCustomAuthToken(uid, claims);
		}
	}
}
