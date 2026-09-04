using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>DepartmentDefault print layout (RMS plan section 4.10.1): generated default, versioned saves, normalization.</summary>
	[TestFixture]
	public class RecordsPrintLayoutServiceTests
	{
		private const int Dept = 6;
		private Mock<IRmsRecordPrintLayoutsRepository> _layouts;
		private RmsRecordPrintLayout _stored;
		private RecordsPrintLayoutService _service;

		[SetUp]
		public void SetUp()
		{
			_stored = null;
			_layouts = new Mock<IRmsRecordPrintLayoutsRepository>();
			_layouts.Setup(l => l.GetAsync(Dept, (int)RmsRecordPrintLayoutScope.DepartmentDefault, string.Empty)).ReturnsAsync(() => _stored);
			_layouts.Setup(l => l.SaveOrUpdateAsync(It.IsAny<RmsRecordPrintLayout>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordPrintLayout row, CancellationToken c, bool b) => { _stored = row; return row; });
			_service = new RecordsPrintLayoutService(_layouts.Object);
		}

		[Test]
		public async Task Unsaved_department_uses_the_generated_default()
		{
			var layout = await _service.GetDepartmentDefaultAsync(Dept);

			layout.Version.Should().Be(0);
			layout.LayoutVersion.Should().Be(RmsRecordPrintLayout.GeneratedLayoutVersion);
			layout.Config.ShowLogo.Should().BeTrue();
			layout.Config.PageSize.Should().Be("Letter");
			layout.Config.LetterheadLine1.Should().BeNull();
		}

		[Test]
		public async Task Saves_are_versioned_and_normalized()
		{
			var first = await _service.SaveDepartmentDefaultAsync(Dept, "admin", new RecordsPrintLayoutConfig { PageSize = "a4", LetterheadLine1 = "  Established 1902  ", WatermarkLabel = new string('W', 60), UseShortName = true });

			first.Version.Should().Be(1);
			first.LayoutVersion.Should().Be("department-default/1");
			first.DefinitionKey.Should().Be(string.Empty);
			first.Config.PageSize.Should().Be("A4");
			first.Config.LetterheadLine1.Should().Be("Established 1902");
			first.Config.WatermarkLabel.Should().HaveLength(40);
			first.ConfigJson.Should().Contain("\"UseShortName\":true");

			var second = await _service.SaveDepartmentDefaultAsync(Dept, "admin", new RecordsPrintLayoutConfig { PageSize = "bogus" });

			second.Version.Should().Be(2);
			second.RmsRecordPrintLayoutId.Should().Be(first.RmsRecordPrintLayoutId);
			second.LayoutVersion.Should().Be("department-default/2");
			second.Config.PageSize.Should().Be("Letter", "unknown sizes fall back to Letter");

			var reloaded = await _service.GetDepartmentDefaultAsync(Dept);
			reloaded.Config.PageSize.Should().Be("Letter");
			reloaded.Version.Should().Be(2);
		}

		[Test]
		public void Corrupt_config_json_falls_back_to_the_default()
		{
			RecordsPrintLayoutService.Parse("{not json").ShowLogo.Should().BeTrue();
			RecordsPrintLayoutService.Parse(null).PageSize.Should().Be("Letter");
		}
	}
}
