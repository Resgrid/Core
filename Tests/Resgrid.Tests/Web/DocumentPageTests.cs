using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Web
{
	[TestFixture]
	public class DocumentPageTests
	{
		[Test]
		public void DescriptionEditor_WhenFormIsSubmitted_PostsToBoundDescriptionField()
		{
			// Arrange
			var scriptPath = FindRepositoryFile("Web/Resgrid.Web/wwwroot/js/app/internal/documents/resgrid.documents.newdocument.js");

			// Act
			var script = File.ReadAllText(scriptPath);

			// Assert
			script.Should().Contain("$('#Description').val(quill.root.innerHTML)");
			script.Should().NotContain("#Document_Description");
		}

		[Test]
		public void ViewDocumentPage_DisplaysMetadataAndPermissionControlledActions()
		{
			// Arrange
			var viewPath = FindRepositoryFile("Web/Resgrid.Web/Areas/User/Views/Documents/ViewDocument.cshtml");

			// Act
			var view = File.ReadAllText(viewPath);

			// Assert
			view.Should().Contain("@Model.Document.Name");
			view.Should().Contain("Model.Document.Category");
			view.Should().Contain("@Html.Raw(Model.DescriptionHtml)");
			view.Should().Contain("@Model.UploadedByName");
			view.Should().Contain("@Model.Document.AddedOn.TimeConverterToString(Model.Department)");
			view.Should().Contain("asp-action=\"GetDocument\"");
			view.Should().Contain("@if (Model.CanEdit)");
			view.Should().Contain("asp-action=\"EditDocument\"");
			view.Should().Contain("@if (Model.CanDelete)");
			view.Should().Contain("asp-action=\"DeleteDocument\"");
			view.Should().Contain("@Html.AntiForgeryToken()");
		}

		[Test]
		public void DocumentsIndex_OpensDetailsPageAndHasNoTrailingBrace()
		{
			// Arrange
			var viewPath = FindRepositoryFile("Web/Resgrid.Web/Areas/User/Views/Documents/Index.cshtml");

			// Act
			var view = File.ReadAllText(viewPath);

			// Assert
			view.Should().Contain("asp-action=\"ViewDocument\"");
			view.TrimEnd().Should().NotEndWith("}");
		}

		private static string FindRepositoryFile(string relativePath)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			if (directory == null)
				throw new InvalidOperationException("Unable to locate the repository root.");

			return Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
		}
	}
}
