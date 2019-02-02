using System.Collections.Generic;
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

		public string CreateToken(string uid, Dictionary<string, object> claims = null)
		{
			return _firebaseAuthProvider.CreateResponderCustomAuthToken(uid, claims).Result;
		}
	}
}
