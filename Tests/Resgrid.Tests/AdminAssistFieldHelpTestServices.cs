using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Resgrid.Model.AdminAssist;

namespace Resgrid.Tests
{
	/// <summary>
	/// What <c>Resgrid.Web.Helpers.AdminAssistFieldTagHelper</c> needs so an in-process MVC harness can render views with
	/// <c>asp-for</c> editors. The tag helper runs on every such editor, so a harness without these fails to activate it
	/// before the page renders. The catalog is empty, so no field help is attached and the markup matches a department
	/// without Admin Assist; the access service is never asked. Harnesses that test field help register their own first.
	/// </summary>
	internal static class AdminAssistFieldHelpTestServices
	{
		public static IServiceCollection AddAdminAssistFieldHelpStubs(this IServiceCollection services)
		{
			var catalog = new Mock<IAdminAssistCatalog>();
			catalog.SetupGet(c => c.Settings).Returns(Array.Empty<SettingCatalogEntry>());
			services.TryAddSingleton(catalog.Object);
			services.TryAddSingleton(new Mock<IAdminAssistAccessService>(MockBehavior.Strict).Object);
			return services;
		}
	}
}
