using System.Collections.Generic;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Resgrid.Config;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Firebase
{
	public class FirebaseAuthProvider : IFirebaseAuthProvider
	{
		private static FirebaseApp _responderFirebaseApp;

		public async Task<string> CreateResponderCustomAuthToken(string userId, Dictionary<string, object> claims = null)
		{
			CreateResponderInstance();
			string customToken = await FirebaseAuth.GetAuth(_responderFirebaseApp).CreateCustomTokenAsync(userId, claims);

			return customToken;
		}

		private void CreateResponderInstance()
		{
			if (_responderFirebaseApp == null)
			{
				AppOptions options = new AppOptions();
				options.ProjectId = FirebaseConfig.ResponderProjectId;
				options.ServiceAccountId = FirebaseConfig.ResponderProjectEmail;
				options.Credential = GoogleCredential.FromJson(FirebaseConfig.ResponderJsonFile);

				_responderFirebaseApp = FirebaseApp.Create(options);
			}
		}
	}
}
