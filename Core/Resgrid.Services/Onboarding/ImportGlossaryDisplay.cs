using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Model.Onboarding;

namespace Resgrid.Services.Onboarding
{
	/// <summary>One line of the glossary drawer: the product's word and what Resgrid calls that kind of record.</summary>
	public sealed class ImportGlossaryRow
	{
		public string Term { get; set; }
		/// <summary>The data set it is imported as, the Resgrid feature it belongs to, or "no equivalent".</summary>
		public string Resgrid { get; set; }
		/// <summary>True when an import brings these records in (the target is a data set).</summary>
		public bool Imported { get; set; }
		/// <summary>True when Resgrid has the kind of record at all (a data set or a feature).</summary>
		public bool Equivalent { get; set; }
		/// <summary>True when the word is offered as the department's own name for the feature on the Configure step.</summary>
		public bool OfferedAsName { get; set; }
	}

	/// <summary>
	/// The glossary drawer and product picker wording (wizard plan sections 6.4 and 7.6), shared by the wizard and the
	/// BackOffice so both say the same thing. Data set names are the wizard's; a feature is named as the department names it.
	/// </summary>
	public sealed class ImportGlossaryDisplay
	{
		private readonly Func<string, string> _import;
		private readonly Func<string, string> _termName;
		private readonly IImportCatalog _catalog;

		/// <param name="import">DepartmentImport strings (null for a missing key).</param>
		/// <param name="termName">A terminology term's name as the department shows it.</param>
		public ImportGlossaryDisplay(Func<string, string> import, Func<string, string> termName, IImportCatalog catalog)
		{
			_import = import;
			_termName = termName;
			_catalog = catalog;
		}

		/// <summary>The English display (BackOffice is English-only by convention), with Resgrid's own names for features.</summary>
		public static ImportGlossaryDisplay English(IImportCatalog catalog)
		{
			var english = CultureInfo.GetCultureInfo("en");
			var resources = new ResourceManager("Resgrid.Localization.Areas.User.DepartmentImport.DepartmentImport", typeof(SupportedLocales).Assembly);
			return new ImportGlossaryDisplay(key =>
			{
				try
				{
					return resources.GetString(key, english);
				}
				catch (MissingManifestResourceException)
				{
					return null;
				}
			}, key => DepartmentTerms.Find(key)?.DefaultName ?? key, catalog);
		}

		private string Import(string key) => _import(key) ?? key;

		/// <summary>The products chosen for a workspace that have a pack, in the order they were chosen.</summary>
		public IReadOnlyList<ImportProductPack> Products(IEnumerable<ImportSource> sources) =>
			(sources ?? Enumerable.Empty<ImportSource>())
			.Where(s => !string.Equals(s.ProductKey, ImportSourceProducts.Continuation, StringComparison.OrdinalIgnoreCase))
			.OrderBy(s => s.CreatedOn).Select(s => _catalog?.GetPack(s.ProductKey)).Where(p => p != null).ToList();

		/// <summary>The words a reader in <paramref name="language"/> sees for a product.</summary>
		public IReadOnlyList<ImportGlossaryRow> Rows(ImportProductPack pack, string language) =>
			ImportGlossary.For(pack, language).Select(entry =>
			{
				var entity = ImportGlossary.EntityOf(entry);
				var term = ImportGlossary.TermOf(entry);
				var contract = entity == null ? null : _catalog?.GetContract(entity);
				return new ImportGlossaryRow
				{
					Term = entry.Term,
					Resgrid = contract != null ? Import(contract.LabelKey) : term != null ? _termName(term) : Import("GlossaryNoEquivalent"),
					Imported = contract != null,
					Equivalent = contract != null || term != null,
					OfferedAsName = entry.Names != null && string.Equals(entry.Language, language, StringComparison.OrdinalIgnoreCase)
				};
			}).ToList();

		/// <summary>The picker badge: whether Resgrid knows the product's words, or has nothing for it but a spreadsheet.</summary>
		public string Status(ImportProductPack pack) => Import(HasGlossary(pack) ? "ProductStatusGlossary" : "ProductStatusNone");

		public static bool HasGlossary(ImportProductPack pack) => pack != null && pack.Kind != "none" && pack.Glossary.Count > 0;
	}
}
