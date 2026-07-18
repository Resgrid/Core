using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Repositories.DataRepository.Extensions;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Models
{
	[TestFixture]
	public class DocumentTests
	{
		[Test]
		public void Category_WhenPersisted_UsesCategoryDatabaseColumn()
		{
			// Arrange
			var document = new Document { Category = "Operations" };

			// Act
			var columns = document
				.GetColumns(new SqlServerConfiguration(), ignoreProperties: document.IgnoredProperties)
				.ToList();

			// Assert
			document.Category.Should().Be("Operations");
			columns.Should().Contain(x => x.Contains("Category", StringComparison.OrdinalIgnoreCase));
			columns.Should().NotContain(x => x.Contains("Catery", StringComparison.OrdinalIgnoreCase));
		}

		[Test]
		public void Description_WhenPersisted_IsIncludedInDocumentColumns()
		{
			// Arrange
			var document = new Document { Description = "<p>Pre-incident plan</p>" };

			// Act
			var columns = document
				.GetColumns(new SqlServerConfiguration(), ignoreProperties: document.IgnoredProperties)
				.ToList();

			// Assert
			document.Description.Should().Be("<p>Pre-incident plan</p>");
			columns.Should().Contain(x => x.Contains("Description", StringComparison.OrdinalIgnoreCase));
		}
	}
}
