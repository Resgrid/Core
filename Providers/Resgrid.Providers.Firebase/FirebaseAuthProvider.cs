using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Resgrid.Config;
using Resgrid.Model.Providers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Providers.Firebase
{
	public class FirebaseAuthProvider : IFirebaseAuthProvider
	{
		private const string _appName = "ResponderAppFirebase";
		private static FirebaseApp _responderFirebaseApp;

		public async Task<string> CreateResponderCustomAuthToken(string userId, Dictionary<string, object> claims = null)
		{
			CreateResponderInstance();
			string customToken = await FirebaseAuth.GetAuth(_responderFirebaseApp).CreateCustomTokenAsync(userId, claims);

			return customToken;
		}

		private void CreateResponderInstance()
		{
			// Try getting the app first
			if (_responderFirebaseApp == null)
			{
				_responderFirebaseApp = FirebaseApp.GetInstance(_appName);
			}

			// If it still doesn't exist create it
			if (_responderFirebaseApp == null)
			{
				AppOptions options = new AppOptions();
				options.ProjectId = FirebaseConfig.ResponderProjectId;
				options.ServiceAccountId = FirebaseConfig.ResponderProjectEmail;
				options.Credential = GoogleCredential.FromJson(FirebaseConfig.ResponderJsonFile);

				_responderFirebaseApp = FirebaseApp.Create(options, _appName);
			}
		}
	}
}
