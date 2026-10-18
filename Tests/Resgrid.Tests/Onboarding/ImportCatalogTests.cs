using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Providers.Import.Catalog;
using Resgrid.Services;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// The embedded import catalog and its entity contracts (wizard plan sections 7.4, 8.3 and 24.1 "Contract tests"): the
	/// catalog validates, dependency order is sound, protected fields match the ADP catalog, writer paths name real
	/// properties, and pinned versions stay loadable.
	/// </summary>
	[TestFixture]
	public class ImportCatalogTests
	{
		private static readonly ImportCatalog Catalog = ImportCatalog.Current;

		[Test]
		public void Embedded_catalog_loads_and_validates()
		{
			Catalog.Version.Should().NotBeNullOrWhiteSpace();
			Catalog.Validate().Should().BeEmpty();
			Catalog.Contracts.Select(c => c.Key).Should().Contain(new[]
			{
				"groups", "roles", "certification-types", "personnel", "personnel-certifications", "unit-types", "units", "custom-statuses",
				"call-types", "call-priorities", "poi-types", "pois"
			});
		}

		[Test]
		public void Dependencies_import_before_their_dependents()
		{
			var order = Catalog.InDependencyOrder().Select(c => c.Key).ToList();
			foreach (var contract in Catalog.Contracts)
				foreach (var dependency in contract.DependsOn.Where(d => d != contract.Key))
					order.IndexOf(dependency).Should().BeLessThan(order.IndexOf(contract.Key), $"{dependency} must import before {contract.Key}");

			order.IndexOf("groups").Should().BeLessThan(order.IndexOf("personnel"));
			order.IndexOf("personnel").Should().BeLessThan(order.IndexOf("personnel-certifications"));
			order.IndexOf("custom-statuses").Should().BeLessThan(order.IndexOf("unit-types"));
		}

		[Test]
		public void Every_protected_field_matches_the_ADP_catalog_and_is_never_internal()
		{
			var adp = new ProtectedFieldCatalog().GetAll().Select(f => f.FieldId).ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (var contract in Catalog.Contracts)
				foreach (var field in contract.Fields.Where(f => f.Protected != null))
				{
					adp.Should().Contain(field.Protected, $"{contract.Key}.{field.Key} names an ADP field");
					field.SensitivityLevel.Should().NotBe(ImportFieldSensitivity.Internal);
				}
		}

		[Test]
		public void Every_writer_path_names_an_existing_target_property()
		{
			var model = typeof(Department).Assembly;
			foreach (var contract in Catalog.Contracts)
				foreach (var field in contract.Fields)
				{
					var parts = field.WriterPath.Split('.');
					parts.Should().HaveCount(2, $"{contract.Key}.{field.Key} writer path is Type.Property");
					var type = model.GetTypes().FirstOrDefault(t => t.Name == parts[0]);
					type.Should().NotBeNull($"{contract.Key}.{field.Key}: type {parts[0]} exists");
					type.GetProperty(parts[1], BindingFlags.Public | BindingFlags.Instance).Should().NotBeNull($"{contract.Key}.{field.Key}: {field.WriterPath} exists");
				}
		}

		[Test]
		public void Choice_codes_are_unique_and_references_point_at_dependencies()
		{
			foreach (var contract in Catalog.Contracts)
			{
				foreach (var field in contract.Fields.Where(f => f.Type == ImportFieldTypes.Choice))
					field.Choices.Select(c => c.Code).Should().OnlyHaveUniqueItems();

				foreach (var field in contract.Fields.Where(f => f.References != null && f.References != contract.Key))
					contract.DependsOn.Should().Contain(field.References);
			}
		}

		[Test]
		public void Pinned_versions_stay_loadable()
		{
			foreach (var version in Catalog.Versions)
				Catalog.GetVersion(version).Should().NotBeNull(version);
			Catalog.GetVersion(null).Should().BeSameAs(Catalog);
			Catalog.GetVersion("1999.1.1").Should().BeNull();
		}

		[Test]
		public void Product_packs_follow_the_D14_catalog()
		{
			Catalog.Packs.Where(p => p.Grade == "A").Select(p => p.Key).Should().Contain(new[]
			{
				"nfirs", "neris", "first-due", "emergency-reporting", "aladtec", "active911", "d4h", "vector-scheduling", "divera247", "gis-layers", "mark43"
			}, "section 7.6.1");
			Catalog.Packs.Where(p => p.Grade == "B").Select(p => p.Key).Should().Contain(new[] { "iamresponding", "eso", "pstrax", "vector-lms", "ukg-telestaff", "ontario-sir" });

			foreach (var pack in Catalog.Packs.Where(p => p.Grade == "A" || p.Grade == "B"))
			{
				pack.SourceUrl.Should().StartWith("https://", pack.Key);
				pack.Glossary.Should().NotBeEmpty(pack.Key);
			}

			foreach (var pack in Catalog.Packs.Where(p => p.Kind == "none"))
				pack.Glossary.Should().BeEmpty("a product without reliable public information gets no pack content ({0})", pack.Key);

			Catalog.Packs.Where(p => p.Published).Should().OnlyContain(p => p.Datasets.Count > 0, "a pack is published once its exports are recognized and tested");
		}

		[Test]
		public void A_glossary_points_only_at_what_Resgrid_has()
		{
			var pack = new ImportProductPack
			{
				Key = "Example", Version = "1", ProductName = "Example", Kind = "file-documented", Grade = "A", SourceUrl = "http://example.com", VerifiedOn = "12/10/2026",
				Glossary =
				{
					new ImportGlossaryEntry { Term = "Members", Language = "en", Target = "entity:personnel", Names = DepartmentTerms.Personnel },
					new ImportGlossaryEntry { Term = "Crew", Language = "en", Target = "entity:crews" },
					new ImportGlossaryEntry { Term = "Rota", Language = "en", Target = "term:module.rota" },
					new ImportGlossaryEntry { Term = "Thing", Language = "en", Target = "somewhere" },
					new ImportGlossaryEntry { Term = "Ding", Language = "xx", Target = "none" },
					new ImportGlossaryEntry { Term = "Staff", Language = "en", Target = "entity:personnel", Names = DepartmentTerms.Personnel },
					new ImportGlossaryEntry { Term = "Units", Language = "en", Target = "entity:units", Names = DepartmentTerms.Units },
					new ImportGlossaryEntry { Term = new string('x', 60), Language = "en", Target = "entity:units", Names = DepartmentTerms.Calls },
					new ImportGlossaryEntry { Term = "members", Language = "en", Target = "none" }
				}
			};
			var reserved = new ImportProductPack { Key = "spreadsheet", Version = "1", ProductName = "Spreadsheet", Kind = "none", Grade = "D", Glossary = { new ImportGlossaryEntry { Term = "Rows", Language = "en", Target = "none" } } };
			var documented = new ImportProductPack { Key = "documented", Version = "1", ProductName = "Documented", Kind = "none", Grade = "A", SourceUrl = "https://example.com", VerifiedOn = "2026-10-12" };
			var published = new ImportProductPack { Key = "published", Version = "1", ProductName = "Published", Kind = "api-extraction", Grade = "A", Published = true, SourceUrl = "https://example.com", VerifiedOn = "2026-10-12" };

			var problems = new ImportCatalog("test", Catalog.Contracts, new[] { pack, reserved, documented, published }).Validate();

			problems.Should().Contain(new[]
			{
				"pack Example: key must be lower-case kebab",
				"pack Example: VerifiedOn must be yyyy-MM-dd",
				"pack Example: SourceUrl must be an https address",
				"pack Example: glossary Crew: unknown target entity:crews",
				"pack Example: glossary Rota: unknown target term:module.rota",
				"pack Example: glossary Thing: unknown target somewhere",
				"pack Example: glossary Ding: unknown language xx",
				"pack Example: glossary Units: is already Resgrid's name for module.units",
				$"pack Example: glossary {new string('x', 60)}: is not a valid name for module.calls",
				"pack Example: duplicate glossary term en|Members",
				"pack Example: two words name en|module.personnel",
				"pack spreadsheet: the key is reserved",
				"pack spreadsheet: a product without a pack has no datasets, glossary or crosswalk and is never published",
				"pack documented: kind none is for grade C or D products, and only for them",
				"pack published: a pack is published only with the exports it recognizes"
			});
		}

		[Test]
		public void A_reader_sees_the_products_words_in_their_language_then_its_English_ones()
		{
			var divera = Catalog.GetPack("divera247");
			var aladtec = Catalog.GetPack("aladtec");

			ImportGlossary.For(divera, "de").Select(e => e.Term).Should().BeEquivalentTo(new[] { "Gruppen", "Fahrzeuge", "Alarme", "Users", "Qualifications" });
			ImportGlossary.For(divera, "en").Should().OnlyContain(e => e.Language == "en");
			ImportGlossary.For(aladtec, "fr").Should().BeEquivalentTo(aladtec.Glossary);

			var names = ImportGlossary.Names(new[] { aladtec, Catalog.GetPack("d4h") }, "en");
			names[DepartmentTerms.Personnel].Text.Should().Be("Members");
			names[DepartmentTerms.Personnel].Products.Should().Equal("aladtec", "d4h");
			ImportGlossary.Names(new[] { Catalog.GetPack("first-due"), divera }, "en").Should().NotContainKey(DepartmentTerms.Units);
			ImportGlossary.Names(new[] { aladtec }, "fr").Should().BeEmpty();
		}

		[Test]
		public void A_snapshot_round_trips_as_an_archived_version()
		{
			var json = Catalog.ToSnapshotJson();
			json.Should().Contain(Catalog.Version);
			json.Should().NotContain("SensitivityLevel", "computed members are not part of the archived contract");
		}
	}
}
