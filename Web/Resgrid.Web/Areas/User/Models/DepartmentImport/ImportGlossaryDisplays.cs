using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Services.Onboarding;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Models.DepartmentImport
{
	/// <summary>The wizard's <see cref="ImportGlossaryDisplay"/>: the reader's language, and features named as the department names them.</summary>
	public static class ImportGlossaryDisplays
	{
		public static ImportGlossaryDisplay For(IStringLocalizer import, DepartmentTerminologyConfig saved, IDepartmentTerminology terms, IImportCatalog catalog) =>
			new ImportGlossaryDisplay(key =>
			{
				var value = import[key];
				return value.ResourceNotFound ? null : value.Value;
			}, key => saved?.TextFor(key) ?? terms.DefaultName(key), catalog);
	}
}
