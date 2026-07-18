using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Web
{
	[TestFixture]
	public class UploadFormEncodingTests
	{
		[TestCaseSource(nameof(UploadViews))]
		public void UploadForm_WithFileInput_UsesMultipartEncoding(string relativePath)
		{
			// Arrange
			var viewPath = FindRepositoryFile(relativePath);

			// Act
			var view = File.ReadAllText(viewPath);

			// Assert
			view.Should().Contain("type=\"file\"");
			view.Should().Contain("enctype=\"multipart/form-data\"");
		}

		private static IEnumerable<string> UploadViews => new[]
		{
			"Web/Resgrid.Web/Areas/User/Views/Documents/NewDocument.cshtml",
			"Web/Resgrid.Web/Areas/User/Views/Documents/EditDocument.cshtml",
			"Web/Resgrid.Web/Areas/User/Views/Profile/AddCertification.cshtml",
			"Web/Resgrid.Web/Areas/User/Views/Profile/EditCertification.cshtml"
		};

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
