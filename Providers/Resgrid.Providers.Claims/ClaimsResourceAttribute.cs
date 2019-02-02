using System;
using System.Security.Claims;

namespace Resgrid.Providers.Claims
{
	[AttributeUsage(AttributeTargets.Class)]
	public class ClaimsResourceAttribute : Attribute
	{
		public string Resrouce { get; set; }

		public ClaimsResourceAttribute(string resource)
		{
			Resrouce = resource;
		}
	}

	[AttributeUsage(AttributeTargets.Class)]
	public class SystemAdminClaimsResourceAttribute : Attribute
	{
		public string Resrouce { get; set; }

		public SystemAdminClaimsResourceAttribute()
		{
			Resrouce = ClaimTypes.Role;
		}
	}
}