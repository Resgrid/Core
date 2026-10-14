using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Moq;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Services;

namespace Resgrid.Tests
{
	/// <summary>
	/// What <c>Resgrid.Web.Helpers.AdminAssistFieldTagHelper</c> needs so an in-process MVC harness can render views with
	/// <c>asp-for</c> editors. The tag helper runs on every such editor, so a harness without these fails to activate it
	/// before the page renders. The catalog is empty, so no field help is attached and the markup matches a department
	/// without Admin Assist; the access service is never asked. Harnesses that test field help register their own first.
	/// It also registers the department terminology every User-area view injects (_ViewImports), with nothing renamed.
	/// </summary>
	internal static class AdminAssistFieldHelpTestServices
	{
		public static IServiceCollection AddAdminAssistFieldHelpStubs(this IServiceCollection services)
		{
			var catalog = new Mock<IAdminAssistCatalog>();
			catalog.SetupGet(c => c.Settings).Returns(Array.Empty<SettingCatalogEntry>());
			services.TryAddSingleton(catalog.Object);
			services.TryAddSingleton(new Mock<IAdminAssistAccessService>(MockBehavior.Strict).Object);
			services.AddDepartmentTerminologyStub();
			return services;
		}

		/// <summary>
		/// Department terminology with nothing renamed: every term shows its shipped name, localized when the harness
		/// registered localization. Harnesses that test renamed terms register their own first.
		/// </summary>
		public static IServiceCollection AddDepartmentTerminologyStub(this IServiceCollection services)
		{
			services.TryAddScoped<Resgrid.Web.Helpers.IDepartmentTerminology>(sp =>
			{
				var settings = new Mock<IDepartmentSettingsService>();
				settings.Setup(s => s.GetDepartmentTerminologyAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new DepartmentTerminologyConfig());
				return new Resgrid.Web.Helpers.DepartmentTerminology(settings.Object,
					sp.GetService<IHttpContextAccessor>() ?? Mock.Of<IHttpContextAccessor>(),
					Localizer<Resgrid.Localization.Common>(sp),
					Localizer<Resgrid.Localization.Areas.User.UserDefinedFields.UserDefinedFields>(sp),
					Localizer<Resgrid.Localization.Areas.User.WorkOrders.WorkOrders>(sp),
					Localizer<Resgrid.Localization.Areas.User.Checklists.Checklists>(sp));
			});
			return services;
		}

		private static IStringLocalizer<T> Localizer<T>(IServiceProvider sp)
		{
			return sp.GetService<IStringLocalizer<T>>() ?? Mock.Of<IStringLocalizer<T>>();
		}
	}
}
