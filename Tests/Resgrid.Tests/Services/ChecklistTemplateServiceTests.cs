using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ChecklistTemplateServiceTests
	{
		[Test]
		public void Catalog_has_stable_unique_item_and_section_identities_and_valid_types()
		{
			var templates = ChecklistTemplateCatalog.All;
			templates.Count.Should().BeGreaterThanOrEqualTo(22);
			templates.Select(t => t.Id).Should().OnlyHaveUniqueItems();
			var sections = templates.SelectMany(t => t.Sections).ToList();
			sections.Select(s => s.SectionId).Should().OnlyHaveUniqueItems();
			var items = sections.SelectMany(s => s.Items).ToList();
			items.Select(i => i.ItemId).Should().OnlyHaveUniqueItems();
			foreach (var template in templates)
			{
				template.Name.Should().NotBeNullOrWhiteSpace();
				template.Sections.Should().NotBeEmpty();
				Enum.IsDefined(template.SuggestedFrequency).Should().BeTrue();
				Enum.IsDefined(template.SuggestedTargetType).Should().BeTrue();
			}
			foreach (var item in items)
			{
				Guid.TryParse(item.ItemId, out _).Should().BeTrue();
				item.Name.Should().NotBeNullOrWhiteSpace();
				Enum.IsDefined(item.Type).Should().BeTrue();
				if (item.Critical)
				{
					item.Required.Should().BeTrue();
					item.AllowNotApplicable.Should().BeFalse();
					item.RequireNoteOnFail.Should().BeTrue();
				}
			}
		}

		[TestCase("Fire")]
		[TestCase("EMS")]
		[TestCase("SAR")]
		[TestCase("Emergency Management")]
		[TestCase("Industry")]
		[TestCase("Business")]
		public void Catalog_covers_each_requested_sector(string sector) => ChecklistTemplateCatalog.Search(sector).Should().NotBeEmpty();

		[Test]
		public void Search_uses_all_terms_and_case_insensitive_identity()
		{
			ChecklistTemplateCatalog.Search("FoRkLiFt, brakes").Select(t => t.Id).Should().Equal("industrial-forklift");
			ChecklistTemplateCatalog.Search("forklift shelter").Should().BeEmpty();
			ChecklistTemplateCatalog.GetById("EMS-CONTROLLED-COUNT").RequiresIndependentWitness.Should().BeTrue();
			ChecklistTemplateCatalog.GetById("unknown").Should().BeNull();
		}

		[Test]
		public async Task Service_denies_catalog_and_direct_template_when_department_is_disabled()
		{
			var access = new Mock<IReadinessAccessService>();
			var service = new ChecklistTemplateService(access.Object);
			(await service.SearchAsync(77)).Should().BeNull();
			(await service.GetByIdAsync(77, "fire-apparatus-daily")).Should().BeNull();
			access.Verify(x => x.CanUseChecklistsAsync(77), Times.Exactly(2));
			access.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Enabled_service_returns_catalog_without_a_maintenance_gate()
		{
			var access = new Mock<IReadinessAccessService>(MockBehavior.Strict);
			access.Setup(x => x.CanUseChecklistsAsync(77)).ReturnsAsync(true);
			var service = new ChecklistTemplateService(access.Object);
			(await service.SearchAsync(77, "shelter opening")).Single().Id.Should().Be("em-shelter");
			(await service.GetByIdAsync(77, "em-eoc")).Should().NotBeNull();
			Func<Task> oversized = () => service.SearchAsync(77, new string('x', 257));
			await oversized.Should().ThrowAsync<ArgumentException>();
		}
	}
}
