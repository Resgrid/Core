using System;
using System.ComponentModel;
using System.Linq;
using System.Security.Claims;
using Resgrid.Model;

namespace Resgrid.Providers.Claims
{
	public class ResgridAuthorizationManager : ClaimsAuthorizationManager
	{
		public override bool CheckAccess(AuthorizationContext context)
		{
			var resource = context.Resource.First();
			var action = context.Action.FirstOrDefault();
			var claims = context.Principal.Claims.ToList();

			if (resource.Type == ClaimTypes.Role)
				return (context.Principal.HasClaim(ClaimTypes.Role, resource.Value));

			switch (resource.Value)
			{
				case ResgridClaimTypes.Resources.Department:
					return CanPerformActionOnDepartment(context);
				case ResgridClaimTypes.Resources.Group:
					return CanPerformActionOnGroup(context);
				default:
					return context.Principal.HasClaim(resource.Value, action.Value);
			}

			return false;
		}

		private bool CanPerformActionOnDepartment(AuthorizationContext context)
		{
			var action = context.Action.FirstOrDefault();

			if (action == null)
				throw new NotSupportedException("Cannot validate claim action check on null action for a department resource.");

			var departments = context.Principal.Claims.Where(x => x.Type == ResgridClaimTypes.Memberships.Departments);

			if (departments.Count() == 1)
				return context.Principal.HasClaim(ResgridClaimTypes.CreateDepartmentClaimTypeString(int.Parse(departments.First().Value)), action.Value);

			return false;
		}

		private bool CanPerformActionOnGroup(AuthorizationContext context)
		{
			var action = context.Action.FirstOrDefault();

			if (context.Resource.Count() < 2)
				return false; // We need the resource, which will be the group claim type, and a 2nd with the group id.

			if (action == null)
				throw new NotSupportedException("Cannot validate claim action check on null action for a department resource.");

			return context.Principal.HasClaim(
				ResgridClaimTypes.CreateGroupClaimTypeString(int.Parse(context.Resource[1].Value)), action.Value);
		}
	}
}
