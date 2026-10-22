using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>The organization type picker shared by sign-up and Department -> Settings.</summary>
	[TestFixture]
	public class DepartmentTypeSelectListTests
	{
		private static IStringLocalizer Localizer()
		{
			var localizer = new Mock<IStringLocalizer>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, "[" + key + "]"));
			return localizer.Object;
		}

		[Test]
		public void Offers_every_catalog_type_once_in_catalog_order_with_translated_names_and_groups()
		{
			var items = DepartmentTypeSelectList.Build(Localizer(), "Security", true);

			items.Select(i => i.Value).Should().Equal(DepartmentTypeCatalog.Values, "a catalog value needs no extra option");
			items.First(i => i.Value == "Community Paramedicine and Mobile Integrated Health").Text.Should().Be("[OrgType.community-paramedicine]");
			items.First(i => i.Value == "Security").Group.Name.Should().Be("[OrgTypeGroup.SecurityAndEvents]");
			items.Select(i => i.Group).Distinct().Should().HaveCount(DepartmentTypeCatalog.All.Select(t => t.Group).Distinct().Count(), "one optgroup per group");
		}

		[Test]
		public void A_type_from_an_older_list_is_kept_as_the_first_option()
		{
			var items = DepartmentTypeSelectList.Build(Localizer(), "Private", true);

			items.First().Value.Should().Be("Private");
			items.First().Text.Should().Be("Private");
			items.First().Group.Should().BeNull();
			items.Skip(1).Select(i => i.Value).Should().Equal(DepartmentTypeCatalog.Values);
		}

		[Test]
		public void A_department_without_a_type_gets_not_set_only_when_asked()
		{
			DepartmentTypeSelectList.Build(Localizer(), null, true).First().Should().Match<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>(i => i.Value == "" && i.Text == "[OrgTypeNotSet]");
			DepartmentTypeSelectList.Build(Localizer(), null, false).Select(i => i.Value).Should().Equal(DepartmentTypeCatalog.Values, "sign-up starts on a real type");
		}

		[Test]
		public void Falls_back_to_the_stored_text_when_a_name_is_missing()
		{
			var localizer = new Mock<IStringLocalizer>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key, resourceNotFound: true));

			var items = DepartmentTypeSelectList.Build(localizer.Object, "EMS", true);

			items.First(i => i.Value == "Bus and Shuttle").Text.Should().Be("Bus and Shuttle");
			items.First(i => i.Value == "EMS").Group.Name.Should().Be(nameof(DepartmentTypeGroups.HealthAndEms));
		}
	}
}
