using System;
using System.Security.Principal;

namespace Resgrid.Web.Services.ApplicationCore.Tokens
{
	public class ResgridPrinciple : IPrincipal
	{
		private IIdentity _identity;
		public ResgridPrinciple(AuthToken authToken)
		{
			AuthToken = authToken;
			IsSystem = false;

			_identity = new GenericIdentity(authToken.UserName, "Basic");
		}

		public AuthToken AuthToken { get; private set; }

		public IIdentity Identity
		{
			get { return _identity; }
		}

		public bool IsInRole(string role)
		{
			throw new NotImplementedException();
		}

		public bool IsSystem { get; set; }
	}
}
