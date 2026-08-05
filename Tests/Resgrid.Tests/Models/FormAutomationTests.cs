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
	public class FormAutomationTests
	{
		[Test]
		public void PersistenceMetadata_UsesStringIdAndExcludesNonColumnProperties()
		{
			var automation = new FormAutomation
			{
				FormAutomationId = Guid.NewGuid().ToString(),
				FormId = Guid.NewGuid().ToString(),
				Form = new Form()
			};

			var columns = automation
				.GetColumns(new SqlServerConfiguration(), ignoreProperties: automation.IgnoredProperties)
				.ToList();

			automation.IdType.Should().Be(1);
			columns.Should().Contain(x => x.Equals("[FormAutomationId]", StringComparison.OrdinalIgnoreCase));
			columns.Should().Contain(x => x.Equals("[FormId]", StringComparison.OrdinalIgnoreCase));
			columns.Should().NotContain(x => x.Contains("IdType", StringComparison.OrdinalIgnoreCase));
			columns.Should().NotContain(x => x.Equals("[Form]", StringComparison.OrdinalIgnoreCase));
		}
	}
}
