using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web
{
	/// <summary>The request-bound terminology the navigation, page titles and record lists read.</summary>
	[TestFixture]
	public class DepartmentTerminologyAccessorTests
	{
		[Test]
		public void Every_catalog_term_has_a_translated_shipped_name()
		{
			DepartmentTerminology.LocalizedTermKeys.Should().BeEquivalentTo(DepartmentTerms.All.Select(t => t.Key));

			var english = CultureInfo.GetCultureInfo("en");
			foreach (var (type, key) in new[]
			{
				(typeof(Resgrid.Localization.Common), "CallsModule"), (typeof(Resgrid.Localization.Common), "RecordsModule"),
				(typeof(Resgrid.Localization.Common), "NavigationReadiness"), (typeof(Resgrid.Localization.Common), "NavigationWorkforceBusiness"),
				(typeof(Resgrid.Localization.Areas.User.WorkOrders.WorkOrders), "WorkOrders"),
				(typeof(Resgrid.Localization.Areas.User.Checklists.Checklists), "Checklists"),
				(typeof(Resgrid.Localization.Areas.User.UserDefinedFields.UserDefinedFields), "RecordTypeUnitActivity"),
				(typeof(Resgrid.Localization.Areas.User.UserDefinedFields.UserDefinedFields), "RecordTypeNerisIncident")
			})
				new ResourceManager(type).GetString(key, english).Should().NotBeNullOrWhiteSpace(type.Name + "." + key);
		}

		[Test]
		public async Task A_department_name_wins_and_an_unrenamed_term_keeps_its_shipped_name()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Run), "Incident");
			config.Set(DepartmentTerms.Units, "Apparatus");
			var terms = DepartmentTerminologyFactory.Create(config);

			(await terms.NameAsync(DepartmentTerms.Units)).Should().Be("Apparatus");
			(await terms.NameAsync(DepartmentTerms.Calls)).Should().Be("Calls");
			(await terms.RecordTypeAsync((int)RmsOperationalRecordType.Run)).Should().Be("Incident");
			(await terms.RecordTypeAsync((int)LogTypes.Run)).Should().Be("Incident", "legacy logs follow the same names");
			(await terms.RecordTypeAsync((int)RmsOperationalRecordType.Callback)).Should().Be("Callback");
		}

		[Test]
		public async Task A_page_heading_keeps_its_own_wording_until_the_module_is_renamed()
		{
			var plain = DepartmentTerminologyFactory.Create();
			(await plain.NameAsync(DepartmentTerms.Reports, "Reporting")).Should().Be("Reporting");

			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.Reports, "Analysis");
			(await DepartmentTerminologyFactory.Create(config).NameAsync(DepartmentTerms.Reports, "Reporting")).Should().Be("Analysis");
		}

		[Test]
		public async Task Department_definitions_show_their_own_name()
		{
			var terms = DepartmentTerminologyFactory.Create();

			(await terms.DefinitionAsync("dept.public-education", "Public Education")).Should().Be("Public Education");
			(await terms.DefinitionAsync("dept.public-education")).Should().Be("dept.public-education");
			(await terms.DefinitionAsync(RmsDefinitionKeys.UnitActivity)).Should().Be("Unit activity");
		}

		[Test]
		public async Task Nobody_signed_in_reads_no_setting()
		{
			var saved = new DepartmentTerminologyConfig();
			saved.Set(DepartmentTerms.Records, "Reports");
			var terms = DepartmentTerminologyFactory.Create(saved, departmentId: 0);
			(await terms.GetAsync()).Terms.Should().BeEmpty();
			(await terms.NameAsync(DepartmentTerms.Records)).Should().Be("Records");
		}
	}
}
