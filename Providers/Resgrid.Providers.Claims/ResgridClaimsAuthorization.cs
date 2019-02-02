using System;
using System.Collections.ObjectModel;
using System.Diagnostics.Contracts;
using System.IdentityModel.Services;
using System.Linq;
using System.Security.Claims;

namespace Resgrid.Providers.Claims
{
	public static class ResgridClaimsAuthorization
	{
		/// <summary>
		/// Default action claim type.
		/// </summary>
		public const string ActionType = "http://application/claims/authorization/action";

		/// <summary>
		/// Default resource claim type
		/// </summary>
		public const string ResourceType = "http://application/claims/authorization/resource";

		public static bool EnforceAuthorizationManagerImplementation { get; set; }

		/// <summary>
		/// Gets the registered authorization manager.
		/// </summary>
		public static ClaimsAuthorizationManager AuthorizationManager
		{
			get
			{
				return FederatedAuthentication.FederationConfiguration.IdentityConfiguration.ClaimsAuthorizationManager;
			}
		}

		static ResgridClaimsAuthorization()
		{
			EnforceAuthorizationManagerImplementation = true;
		}

		/// <summary>
		/// Checks the authorization policy.
		/// </summary>
		/// <param name="resource">The resource.</param>
		/// <param name="action">The action.</param>
		/// <returns>true when authorized, otherwise false</returns>
		public static bool CheckAccess(string action, params string[] resources)
		{
			Contract.Requires(!String.IsNullOrEmpty(action));


			return CheckAccess(ClaimsPrincipal.Current, action, resources);
		}

		public static bool CheckAccess(ClaimsPrincipal principal, string action, params string[] resources)
		{
			var context = CreateAuthorizationContext(
					principal,
					action,
					resources);

			return CheckAccess(context);
		}

		/// <summary>
		/// Checks the authorization policy.
		/// </summary>
		/// <param name="actions">The actions.</param>
		/// <param name="resources">The resources.</param>
		/// <returns>true when authorized, otherwise false</returns>
		public static bool CheckAccess(Collection<Claim> actions, Collection<Claim> resources)
		{
			Contract.Requires(actions != null);
			Contract.Requires(resources != null);


			return CheckAccess(new AuthorizationContext(
					ClaimsPrincipal.Current, resources, actions));
		}

		/// <summary>
		/// Checks the authorization policy.
		/// </summary>
		/// <param name="action">The action.</param>
		/// <param name="resources">The resources.</param>
		/// <returns>true when authorized, otherwise false</returns>
		public static bool CheckAccess(string action, params Claim[] resources)
		{
			Contract.Requires(action != null);
			Contract.Requires(resources != null);

			var actionCollection = new Collection<Claim>();
			actionCollection.Add(new Claim(ActionType, action));
			var resourceCollection = new Collection<Claim>();
			foreach (var resource in resources) resourceCollection.Add(resource);

			return CheckAccess(new AuthorizationContext(
					ClaimsPrincipal.Current, resourceCollection, actionCollection));
		}

		/// <summary>
		/// Checks the authorization policy.
		/// </summary>
		/// <param name="action">The action.</param>
		/// <param name="resource">The resource name.</param>
		/// <param name="resources">The resources.</param>
		/// <returns>true when authorized, otherwise false</returns>
		public static bool CheckAccess(string action, string resource, params Claim[] resources)
		{
			Contract.Requires(action != null);
			Contract.Requires(resource != null);

			var resourceList = resources.ToList();
			resourceList.Add(new Claim(ResourceType, resource));
			return CheckAccess(action, resourceList.ToArray());
		}

		/// <summary>
		/// Checks the authorization policy.
		/// </summary>
		/// <param name="context">The authorization context.</param>
		/// <returns>true when authorized, otherwise false</returns>
		public static bool CheckAccess(AuthorizationContext context)
		{
			Contract.Requires(context != null);


			if (EnforceAuthorizationManagerImplementation)
			{
				var authZtype = AuthorizationManager.GetType().FullName;
				if (authZtype.Equals("System.Security.Claims.ClaimsAuthorizationManager"))
				{
					throw new InvalidOperationException("No ClaimsAuthorizationManager implementation configured.");
				}
			}

			return AuthorizationManager.CheckAccess(context);
		}

		public static AuthorizationContext CreateAuthorizationContext(ClaimsPrincipal principal, string action, params string[] resources)
		{
			var actionClaims = new Collection<Claim>
						{
								new Claim(ActionType, action)
						};

			var resourceClaims = new Collection<Claim>();

			if (resources != null && resources.Length > 0)
			{
				resources.ToList().ForEach(ar => resourceClaims.Add(new Claim(ResourceType, ar)));
			}

			return new AuthorizationContext(
					principal,
					resourceClaims,
					actionClaims);
		}
	}
}
