using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services.Records;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Definition-scope print layouts (RMS plan section 4.10.1): normalized versioned saves, resolution against the
	/// department default and the definition version, layout-aware rendering, and the designer view model round trip.
	/// </summary>
	[TestFixture]
	public class RecordsDefinitionLayoutTests
	{
		private const int Dept = 6;
		private Mock<IRmsRecordPrintLayoutsRepository> _layouts;
		private Dictionary<string, RmsRecordPrintLayout> _stored;
		private RecordsPrintLayoutService _service;

		[SetUp]
		public void SetUp()
		{
			_stored = new Dictionary<string, RmsRecordPrintLayout>();
			_layouts = new Mock<IRmsRecordPrintLayoutsRepository>();
			_layouts.Setup(l => l.GetAsync(Dept, It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync((int d, int scope, string key) => _stored.TryGetValue(scope + "|" + key, out var row) ? row : null);
			_layouts.Setup(l => l.SaveOrUpdateAsync(It.IsAny<RmsRecordPrintLayout>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsRecordPrintLayout row, CancellationToken c, bool b) => { _stored[row.Scope + "|" + row.DefinitionKey] = row; return row; });
			_service = new RecordsPrintLayoutService(_layouts.Object);
		}

		private static RecordDefinitionSchema Schema() => RmsDefinitionHarness.Schema(
			RmsDefinitionHarness.Section("summary", "Summary", RmsDefinitionHarness.Field("title", RmsFieldType.ShortText), RmsDefinitionHarness.Field("internal_note", RmsFieldType.LongText)),
			RmsDefinitionHarness.Rows("crew", "Crew", null, null, RmsDefinitionHarness.Field("member", RmsFieldType.ShortText), RmsDefinitionHarness.Field("hours", RmsFieldType.Integer)),
			RmsDefinitionHarness.Section("signoff", "Sign-off", RmsDefinitionHarness.Field("signature", RmsFieldType.Signature)));

		[Test]
		public async Task Definition_layout_saves_are_versioned_normalized_and_resolve_only_for_the_version_they_apply_to()
		{
			(await _service.ResolveForDefinitionAsync(Dept, "shift-log", 1)).Definition.Should().BeNull("nothing saved yet resolves to the department default alone");

			var saved = await _service.SaveDefinitionLayoutAsync(Dept, "admin", "Shift-Log", new RecordsDefinitionLayoutConfig
			{
				SectionOrder = new List<string> { " Signoff ", "summary", "summary" }, HiddenFieldKeys = new List<string> { "Internal_Note" },
				SignatureBlockPlacement = "bogus", AttachmentListStyle = "LIST", AppliesToVersion = 2,
				BrandingOverrides = new RecordsPrintLayoutConfig { PageSize = "a4" }
			});

			saved.Version.Should().Be(1);
			saved.LayoutVersion.Should().Be("shift-log/1");
			saved.DefinitionConfig.SectionOrder.Should().Equal("signoff", "summary");
			saved.DefinitionConfig.HiddenFieldKeys.Should().Equal("internal_note");
			saved.DefinitionConfig.SignatureBlockPlacement.Should().Be(RecordsDefinitionLayoutConfig.SignatureAtEnd);
			saved.DefinitionConfig.AttachmentListStyle.Should().Be(RecordsDefinitionLayoutConfig.AttachmentsList);

			var other = await _service.ResolveForDefinitionAsync(Dept, "shift-log", 1);
			other.Definition.Should().BeNull("the layout is pinned to version 2");
			other.LayoutVersion.Should().Be(RmsRecordPrintLayout.GeneratedLayoutVersion);

			var resolved = await _service.ResolveForDefinitionAsync(Dept, "shift-log", 2);
			resolved.Definition.Should().NotBeNull();
			resolved.DefinitionLayoutVersion.Should().Be("shift-log/1");
			resolved.Branding.PageSize.Should().Be("A4", "branding overrides replace the department block");
			resolved.LayoutVersion.Should().Be("shift-log/1+shift-log/1/branding");

			var second = await _service.SaveDefinitionLayoutAsync(Dept, "admin", "shift-log", new RecordsDefinitionLayoutConfig());
			second.Version.Should().Be(2);
			second.RmsRecordPrintLayoutId.Should().Be(saved.RmsRecordPrintLayoutId);
			(await _service.ResolveForDefinitionAsync(Dept, "shift-log", 7)).LayoutVersion.Should().Be("shift-log/2+" + RmsRecordPrintLayout.GeneratedLayoutVersion, "an unpinned layout applies to every version and keeps the department branding");
			(await _service.ResolveForDefinitionAsync(Dept, RmsDefinitionKeys.NerisIncidentReport, 1)).Definition.Should().BeNull("locked definitions never take a definition layout");
		}

		[Test]
		public void Layout_rendering_orders_sections_hides_fields_renames_headings_breaks_pages_and_moves_signatures()
		{
			var values = JObject.Parse("{\"Summary\":{\"title\":\"Night shift\",\"internal_note\":\"do not print\"},\"Crew\":[{\"member\":\"A\",\"hours\":\"4\"},{\"member\":\"B\",\"hours\":\"2\"}],\"Sign-off\":{\"signature\":\"signed:abc\"}}");
			var layout = new RecordsDefinitionLayoutConfig
			{
				SectionOrder = new List<string> { "crew", "summary" }, HiddenFieldKeys = new List<string> { "internal_note" },
				SectionHeadings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["crew"] = "Crew hours" }, PageBreakBeforeSectionKeys = new List<string> { "summary" }
			};
			var html = new StringBuilder();

			RecordsDocumentService.RenderValuesWithLayout(html, values, Schema(), layout);
			var text = html.ToString();

			text.IndexOf("Crew hours", StringComparison.Ordinal).Should().BePositive().And.BeLessThan(text.IndexOf("Night shift", StringComparison.Ordinal), "crew renders before summary");
			text.Should().NotContain("do not print").And.Contain("<h2 style=\"page-break-before:always\">Summary</h2>");
			text.Should().Contain("<h2>Signatures</h2>").And.Contain("Sign-off / signature");
			text.IndexOf("Signatures", StringComparison.Ordinal).Should().BeGreaterThan(text.IndexOf("Night shift", StringComparison.Ordinal), "signature blocks print at the end by default");
			text.Should().Contain("<th>member</th>").And.Contain("<td>2</td>", "repeating rows render as a numbered table");

			var inline = new StringBuilder();
			RecordsDocumentService.RenderValuesWithLayout(inline, values, Schema(), new RecordsDefinitionLayoutConfig { SignatureBlockPlacement = RecordsDefinitionLayoutConfig.SignatureInline, HiddenSectionKeys = new List<string> { "crew" } });
			inline.ToString().Should().NotContain("Signatures").And.Contain("<th>signature</th>").And.NotContain("Crew");
		}

		[Test]
		public void Designer_view_model_round_trips_the_configuration()
		{
			var aggregate = new RecordDefinitionAggregate
			{
				Definition = new RmsRecordDefinition { DefinitionKey = "shift-log", Name = "Shift log", Owner = (int)RmsDefinitionOwner.Department },
				Versions = new List<RmsRecordDefinitionVersion> { new RmsRecordDefinitionVersion { Version = 1, State = (int)RmsDefinitionVersionState.Published, Schema = Schema() } }
			};
			var stored = new RmsRecordPrintLayout
			{
				Scope = (int)RmsRecordPrintLayoutScope.Definition, DefinitionKey = "shift-log", Version = 3,
				DefinitionConfig = new RecordsDefinitionLayoutConfig
				{
					SectionOrder = new List<string> { "signoff", "crew" }, HiddenSectionKeys = new List<string> { "crew" }, HiddenFieldKeys = new List<string> { "internal_note" },
					SectionHeadings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["summary"] = "Overview" }, PageBreakBeforeSectionKeys = new List<string> { "signoff" },
					AppliesToVersion = 1, SignatureBlockPlacement = RecordsDefinitionLayoutConfig.SignatureNone, AttachmentListStyle = RecordsDefinitionLayoutConfig.AttachmentsNone,
					BrandingOverrides = new RecordsPrintLayoutConfig { WatermarkLabel = "DRAFT" }
				}
			};

			var model = RecordDefinitionLayoutView.From(aggregate, aggregate.Versions[0], stored, "department-default/2");

			model.LayoutVersion.Should().Be("shift-log/3");
			model.DepartmentLayoutVersion.Should().Be("department-default/2");
			model.OrderedSectionKeys().Should().Equal(new[] { "signoff", "crew", "summary" }, "hidden sections stay listed so they can be shown again; unmentioned sections follow");
			model.Visible["crew"].Should().BeFalse();
			model.OverrideBranding.Should().BeTrue();
			model.Versions.Should().ContainSingle(v => v.Value == "1");

			var config = model.ToConfig();
			config.SectionOrder.Should().Equal("signoff", "crew", "summary");
			config.HiddenSectionKeys.Should().Equal("crew");
			config.HiddenFieldKeys.Should().Equal("internal_note");
			config.SectionHeadings["summary"].Should().Be("Overview");
			config.PageBreakBeforeSectionKeys.Should().Equal("signoff");
			config.AppliesToVersion.Should().Be(1);
			config.SignatureBlockPlacement.Should().Be(RecordsDefinitionLayoutConfig.SignatureNone);
			config.BrandingOverrides.WatermarkLabel.Should().Be("DRAFT");

			model.OverrideBranding = false;
			model.ToConfig().BrandingOverrides.Should().BeNull();
		}
	}
}
