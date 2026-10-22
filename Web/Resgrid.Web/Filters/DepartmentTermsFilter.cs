using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Filters
{
	/// <summary>
	/// Reads the signed-in department's terminology (setting 118, cached) before the action runs, so every localized string the
	/// action and its view produce carries the department's words (<see cref="DepartmentTermStringLocalizerFactory"/>).
	/// </summary>
	public sealed class DepartmentTermsFilter : IAsyncResourceFilter
	{
		public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
		{
			var terms = context.HttpContext.RequestServices?.GetService<IDepartmentTerminology>();
			if (terms != null)
			{
				if (context.Filters.OfType<SuppressDepartmentTermsAttribute>().Any())
					terms.Suppress();
				else
					await terms.PrepareAsync();
			}

			await next();
		}
	}

	/// <summary>
	/// Shows Resgrid's own wording on a page: the Terminology settings page names the shipped terms it lets a department replace,
	/// so its text must not be replaced itself.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
	public sealed class SuppressDepartmentTermsAttribute : Attribute, IFilterMetadata
	{
	}
}
