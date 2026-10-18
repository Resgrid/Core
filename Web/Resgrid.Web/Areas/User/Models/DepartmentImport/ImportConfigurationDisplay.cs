using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Services.Onboarding;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Models.DepartmentImport
{
	/// <summary>The Configure tab's <see cref="ImportConfigurationDisplay"/> in the reader's language (the view's localizers).</summary>
	public static class ImportConfigurationDisplays
	{
		public static ImportConfigurationDisplay For(IStringLocalizer import, IStringLocalizer security, IStringLocalizer department, IStringLocalizer assist,
			IDepartmentTerminology terms, IReadOnlyList<PersonnelRole> roles, IReadOnlyList<ImportArtifact> logos, IImportCatalog catalog) =>
			new ImportConfigurationDisplay(Lookup(import), Lookup(security), Lookup(department), Lookup(assist), terms.DefaultName, roles, logos, catalog);

		private static Func<string, string> Lookup(IStringLocalizer localizer) => key =>
		{
			var value = localizer[key];
			return value.ResourceNotFound ? null : value.Value;
		};
	}
}
