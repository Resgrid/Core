using System.IO;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web
{
	[TestFixture]
	public class WorkspaceFormHelperTests
	{
		[Test]
		public void Field_WithParentedChoices_RendersTheContractTheWorkspaceScriptFiltersOn()
		{
			// Arrange
			var options = WorkspaceFormHelper.Choices(new[] { ("lot-a1", "A-1", "item-a"), ("lot-b1", "B-1", "item-b") });

			// Act
			var html = Render(WorkspaceFormHelper.Field(Localizer(), "Lines[0].LotId", "LotId", options: options, filterBy: "Lines[0].ItemId"));

			// Assert
			html.Should().Contain("data-rgw-filter-by=\"Lines[0].ItemId\"");
			html.Should().Contain("<option data-rgw-parent=\"item-a\" value=\"lot-a1\">A-1</option>");
			html.Should().Contain("<option data-rgw-parent=\"item-b\" value=\"lot-b1\">B-1</option>");
			// The blank entry carries no parent, so it stays on offer whatever item is chosen.
			html.Should().Contain("<option selected=\"selected\" value=\"\">");
		}

		[Test]
		public void Field_WithoutFilterBy_RendersNoDependentSelectAttributes()
		{
			// Arrange
			var options = WorkspaceFormHelper.Choices(new[] { ("item-a", "Gloves") });

			// Act
			var html = Render(WorkspaceFormHelper.Field(Localizer(), "ItemId", "Items", options: options));

			// Assert
			html.Should().NotContain("data-rgw-filter-by");
			html.Should().NotContain("data-rgw-parent");
		}

		private static IStringLocalizer Localizer()
		{
			var localizer = new Mock<IStringLocalizer>();
			localizer.Setup(x => x[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key, true));
			return localizer.Object;
		}

		private static string Render(IHtmlContent content)
		{
			using var writer = new StringWriter();
			content.WriteTo(writer, HtmlEncoder.Default);
			return writer.ToString();
		}
	}
}
