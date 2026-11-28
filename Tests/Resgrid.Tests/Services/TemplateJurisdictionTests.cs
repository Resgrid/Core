using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>Industrial ERT G8 CA1 (jurisdiction on template catalogs) and CA2 (content held for reviewer sign-off).</summary>
	[TestFixture, NonParallelizable]
	public class TemplateJurisdictionTests
	{
		[TestCase(null, "CA-ON", true)]
		[TestCase("CA", null, true)]
		[TestCase("CA", "CA-ON", true)]
		[TestCase("CA-ON", "CA-ON", true)]
		[TestCase("CA-ON", "CA", true)]
		[TestCase("CA-ON", "CA-BC", false)]
		[TestCase("CA-BC", "CA-ON", false)]
		[TestCase("US", "CA-ON", false)]
		[TestCase("EU", "DE", true)]
		[TestCase("EU", "GB", false)]
		[TestCase("GB", "GB", true)]
		[TestCase("ca-on", "CA-ON", true)]
		public void A_template_fits_its_country_and_subdivision_and_generic_fits_everywhere(string template, string department, bool fits)
			=> TemplateJurisdictions.Fits(template, department).Should().Be(fits);

		[TestCase("Canada", "Ontario", "CA-ON")]
		[TestCase("CA", "ON", "CA-ON")]
		[TestCase("CAN", "ontario", "CA-ON")]
		[TestCase("canada", null, "CA")]
		[TestCase("Canada", "Somewhere", "CA")]
		[TestCase("USA", "Texas", "US-TX")]
		[TestCase("United States", "tx", "US-TX")]
		[TestCase("Germany", "Bayern", "DE")]
		[TestCase("Atlantis", "ON", null)]
		[TestCase(null, "Ontario", null)]
		[TestCase("  ", null, null)]
		public void A_free_text_address_becomes_a_code(string country, string state, string expected)
			=> TemplateJurisdictions.FromAddress(country, state).Should().Be(expected);

		[Test]
		public void Region_specific_certification_templates_are_tagged_and_professional_qualifications_stay_general()
		{
			CertificationTypeTemplateCatalog.GetById("on-security").Jurisdiction.Should().Be("CA-ON");
			CertificationTypeTemplateCatalog.GetById("bc-swl").Jurisdiction.Should().Be("CA-BC");
			CertificationTypeTemplateCatalog.GetById("ca-whmis").Jurisdiction.Should().Be("CA");
			CertificationTypeTemplateCatalog.GetById("nremt-emt").Jurisdiction.Should().Be("US");
			CertificationTypeTemplateCatalog.GetById("eu-faw").Jurisdiction.Should().Be("EU");
			CertificationTypeTemplateCatalog.GetById("ff1").Jurisdiction.Should().BeNull("NFPA professional qualifications are used across North America");
			CertificationTypeTemplateCatalog.Jurisdictions.Keys.Should().OnlyContain(code => CertificationTypeTemplateCatalog.GetById(code.ToLowerInvariant()) != null, "every tag names a real template");

			var ontario = CertificationTypeTemplateCatalog.ForJurisdiction(CertificationTypeTemplateCatalog.All, "CA-ON");
			ontario.Should().Contain(t => t.Code == "ON-SECURITY").And.Contain(t => t.Code == "CA-WHMIS").And.Contain(t => t.Code == "FF1");
			ontario.Should().NotContain(t => t.Code == "BC-SWL").And.NotContain(t => t.Code == "NREMT-EMT").And.NotContain(t => t.Code == "UK-SIA");
			CertificationTypeTemplateCatalog.ForJurisdiction(CertificationTypeTemplateCatalog.All, null).Should().HaveCount(CertificationTypeTemplateCatalog.All.Count);
		}

		[Test]
		public void Localized_templates_keep_their_jurisdiction()
		{
			CertificationTypeTemplateCatalog.AllFor("de").Single(t => t.Code == "ON-SECURITY").Jurisdiction.Should().Be("CA-ON");
			TrainingCategoryTemplateCatalog.AllFor("fr").Single(t => t.Key == TrainingCategoryTemplateCatalog.CanadaWorkplace).Jurisdiction.Should().Be("CA");
		}

		[Test]
		public void Training_category_region_templates_are_tagged()
		{
			TrainingCategoryTemplateCatalog.Find(TrainingCategoryTemplateCatalog.CanadaWorkplace).Jurisdiction.Should().Be("CA");
			TrainingCategoryTemplateCatalog.Find(TrainingCategoryTemplateCatalog.NremtNccp).Jurisdiction.Should().Be("US");
			TrainingCategoryTemplateCatalog.Find(TrainingCategoryTemplateCatalog.IndustrialSafety).Jurisdiction.Should().BeNull();
			TrainingCategoryTemplateCatalog.ForJurisdiction(TrainingCategoryTemplateCatalog.All, "CA-ON").Should().NotContain(t => t.Key == TrainingCategoryTemplateCatalog.EuWorkplace);
		}

		[Test]
		public void Unreviewed_jurisdiction_packs_are_never_offered_unless_the_operator_shows_them()
		{
			// ERT-D10: Canadian/Ontario content stays out of the catalogs until a reviewer signs it off.
			ContentPacksConfigScope(false, () =>
			{
				ChecklistTemplateCatalog.All.Should().NotContain(t => t.Id == "ca-on-extinguisher-monthly");
				ChecklistTemplateCatalog.GetById("post-hot-work-fire-watch").Should().BeNull();
				CertificationTypeTemplateCatalog.GetById("on-wah").Should().BeNull();
				CertificationTypeTemplateCatalog.Search("brigade").Should().NotContain(t => t.Code.StartsWith("NFPA1081"));
			});
			ContentPacksConfigScope(true, () =>
			{
				var template = ChecklistTemplateCatalog.GetById("ca-on-extinguisher-monthly");
				template.Should().NotBeNull(); template.Jurisdiction.Should().Be("CA-ON"); ChecklistTemplateCatalog.IsUnreviewed(template).Should().BeTrue();
				CertificationTypeTemplateCatalog.GetById("on-wah").Jurisdiction.Should().Be("CA-ON");
				CertificationTypeTemplateCatalog.IsUnreviewed(CertificationTypeTemplateCatalog.GetById("nfpa1081-is")).Should().BeTrue();
				CertificationTypeTemplateCatalog.IsUnreviewed(CertificationTypeTemplateCatalog.GetById("ff1")).Should().BeFalse();
			});
		}

		[Test]
		public void Pending_content_is_well_formed_cites_its_basis_and_applies_with_new_ids()
		{
			var all = ChecklistTemplateCatalog.All.Concat(ChecklistTemplateCatalog.PendingReview).ToList();
			all.Select(t => t.Id).Should().OnlyHaveUniqueItems();
			all.SelectMany(t => t.Sections).SelectMany(s => s.Items).Select(i => i.ItemId).Should().OnlyHaveUniqueItems();
			foreach (var template in ChecklistTemplateCatalog.PendingReview)
			{
				template.Basis.Should().NotBeNullOrWhiteSpace(template.Id + " must say what it is paraphrased from");
				template.Basis.Should().NotContain("complies", "packs support record keeping, they never claim compliance");
				var form = ChecklistForm.FromTemplate(template);
				ChecklistValidation.Validate(form).Should().BeEmpty(template.Name);
			}
			var certificates = CertificationTypeTemplateCatalog.All.Concat(CertificationTypeTemplateCatalog.PendingReview).ToList();
			certificates.Select(t => t.Code).Should().OnlyHaveUniqueItems();
			CertificationTypeTemplateCatalog.PendingReview.Where(t => t.Code.StartsWith("ON-")).Should().OnlyContain(t => t.Jurisdiction == "CA-ON");
		}

		[Test]
		public async Task Department_jurisdiction_comes_from_its_address_and_never_breaks_a_page()
		{
			var departments = new Mock<IDepartmentsService>(); var addresses = new Mock<IAddressService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(5, false)).ReturnsAsync(new Department { DepartmentId = 5, AddressId = 9 });
			addresses.Setup(a => a.GetAddressByIdAsync(9)).ReturnsAsync(new Address { Country = "Canada", State = "Ontario" });
			var service = new DepartmentJurisdictionService(departments.Object, addresses.Object);
			(await service.GetJurisdictionAsync(5)).Should().Be("CA-ON");
			departments.Setup(d => d.GetDepartmentByIdAsync(6, false)).ThrowsAsync(new InvalidOperationException("cache down"));
			(await service.GetJurisdictionAsync(6)).Should().BeNull("an unreadable address only means the catalog shows everything");
			(await service.GetJurisdictionAsync(0)).Should().BeNull();
		}

		private static void ContentPacksConfigScope(bool show, Action body)
		{
			var saved = Resgrid.Config.ContentPacksConfig.ShowUnreviewedPacks;
			try { Resgrid.Config.ContentPacksConfig.ShowUnreviewedPacks = show; body(); }
			finally { Resgrid.Config.ContentPacksConfig.ShowUnreviewedPacks = saved; }
		}
	}
}
