using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.CostRecovery;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05 item 2.14 (Cal OES MARS): the Salary Survey draft decrypts members' pay, so it also needs
	/// ViewWorkforceCompensation and never drafts a classification smaller than the minimum group size; and a rostered member
	/// without MutualAidReimbursement_View prints and downloads their own draft without the expected reimbursement, as v4 does.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class BusinessOpsMarsTests
	{
		private const int Dept = 71;
		private const string Me = "mars-user";
		private const string Section = "<!--rg:expected-reimbursement--><h2>Expected reimbursement</h2><table><tr><td>Rate</td><td>61.33</td></tr></table><!--/rg:expected-reimbursement-->";

		private DefaultHttpContext _http;
		private Mock<ICalOesMarsService> _mars;

		[SetUp]
		public void SetUp()
		{
			_http = new DefaultHttpContext { Connection = { RemoteIpAddress = System.Net.IPAddress.Loopback }, User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Me), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) }, "test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };
			_mars = new Mock<ICalOesMarsService>();
			_mars.Setup(x => x.GetWorkItemAsync("f42", Dept)).ReturnsAsync(new CalOesMarsWorkItem { CalOesMarsWorkItemId = "f42", DepartmentId = Dept, RecordType = (int)CalOesMarsRecordTypes.F42 });
			_mars.Setup(x => x.GetWorkItemAsync("inv", Dept)).ReturnsAsync(new CalOesMarsWorkItem { CalOesMarsWorkItemId = "inv", DepartmentId = Dept, RecordType = (int)CalOesMarsRecordTypes.GeneratedInvoice });
			_mars.Setup(x => x.IsRosteredForWorkItemAsync(It.IsAny<string>(), Dept, Me)).ReturnsAsync(true);
			_mars.Setup(x => x.RenderWorkItemHtmlAsync("f42", Dept)).ReturnsAsync("<html><body><table><tr><td>F-42</td></tr></table>" + Section + "</body></html>");
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private void Grant(string resource, string action) => _http.User.AddIdentity(new ClaimsIdentity(new[] { new Claim(resource, action) }));

		private CalOesMarsController Controller()
		{
			var constructor = typeof(CalOesMarsController).GetConstructors().Single();
			var controller = (CalOesMarsController)constructor.Invoke(constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(ICalOesMarsService) ? _mars.Object : ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray());
			controller.ControllerContext = new ControllerContext { HttpContext = _http };
			controller.TempData = new TempDataDictionary(_http, Mock.Of<ITempDataProvider>());
			return controller;
		}

		[Test]
		public async Task The_salary_survey_draft_needs_ViewWorkforceCompensation_on_top_of_MARS_management()
		{
			Grant(ResgridClaimTypes.Resources.MutualAidReimbursement, ResgridClaimTypes.Actions.Update);

			(await Controller().BuildSalarySurvey("rate-1", null)).Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/Public/Unauthorized");
			_mars.Verify(x => x.BuildSalarySurveyDraftAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_rostered_member_prints_their_draft_without_the_expected_reimbursement()
		{
			var content = (await Controller().Print("f42")).Should().BeOfType<ContentResult>().Subject.Content;

			content.Should().Contain("F-42");
			content.Should().NotContain("Expected reimbursement").And.NotContain("61.33");
			(await Controller().Print("inv")).Should().BeOfType<RedirectResult>("an observed MARS invoice is never a rostered member's record");
		}

		[Test]
		public async Task A_MARS_viewer_still_prints_the_expected_reimbursement()
		{
			Grant(ResgridClaimTypes.Resources.MutualAidReimbursement, ResgridClaimTypes.Actions.View);

			(await Controller().Print("f42")).Should().BeOfType<ContentResult>().Which.Content.Should().Contain("61.33");
		}

		[Test]
		public async Task A_rostered_members_evidence_packet_drops_the_reimbursement_section_and_keeps_every_other_file()
		{
			_mars.Setup(x => x.BuildEvidencePacketAsync("f42", Dept, Me)).ReturnsAsync(Zip(("README.txt", "readme"), ("record.html", "<html><body>F-42" + Section + "</body></html>"), ("supporting/receipt.pdf", "pdf")));

			var file = (await Controller().Packet("f42")).Should().BeOfType<FileContentResult>().Subject;

			var entries = Read(file.FileContents);
			entries.Keys.Should().BeEquivalentTo(new[] { "README.txt", "record.html", "supporting/receipt.pdf" });
			entries["record.html"].Should().Contain("F-42").And.NotContain("61.33");
			entries["supporting/receipt.pdf"].Should().Be("pdf");
		}

		[Test]
		public void The_redaction_only_removes_the_marked_section()
		{
			CalOesMarsService.WithoutExpectedReimbursement("<p>a</p>" + Section + "<p>b</p>").Should().Be("<p>a</p><p>b</p>");
			CalOesMarsService.WithoutExpectedReimbursement("<p>nothing calculated</p>").Should().Be("<p>nothing calculated</p>");
		}

		[Test]
		public async Task Classifications_below_the_minimum_group_size_are_never_drafted()
		{
			CalOesMarsService.MinimumClassificationGroupSize.Should().Be(3);
			var compensation = new Mock<ICompensationCostService>();
			compensation.Setup(c => c.GetClassificationRateAggregateAsync(Dept, It.IsAny<DateTime>(), It.IsAny<string>()))
				.ReturnsAsync(new List<(string, int, decimal, decimal)> { ("Captain", 2, 61m, 0m), ("Firefighter", 1, 40m, 0m) });
			var profiles = new Mock<ICalOesMarsRateProfileRepository>();
			profiles.Setup(r => r.GetByIdForDepartmentAsync("rate-1", Dept)).ReturnsAsync(new CalOesMarsRateProfile { CalOesMarsRateProfileId = "rate-1", DepartmentId = Dept, SubmissionType = (int)CalOesMarsSubmissionTypes.SalarySurvey, Status = (int)CalOesMarsRateProfileStatuses.Draft });
			var lines = new Mock<ICalOesMarsRateLineRepository>();
			var constructor = typeof(CalOesMarsService).GetConstructors().Single();
			var service = (CalOesMarsService)constructor.Invoke(constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(ICalOesMarsRateProfileRepository) ? profiles.Object :
				p.ParameterType == typeof(ICalOesMarsRateLineRepository) ? lines.Object :
				p.ParameterType == typeof(Lazy<ICompensationCostService>) ? new Lazy<ICompensationCostService>(() => compensation.Object) :
				p.ParameterType.IsInterface ? ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object : (object)null).ToArray());

			var draft = await service.BuildSalarySurveyDraftAsync("rate-1", Dept, new DateTime(2026, 6, 1), Me, null, null);

			draft.Blockers.Should().Equal("classifications_too_small");
			draft.Classifications.Should().BeEmpty();
			lines.Verify(r => r.SaveOrUpdateAsync(It.IsAny<CalOesMarsRateLine>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
		}

		private static byte[] Zip(params (string Name, string Text)[] files)
		{
			using var stream = new MemoryStream();
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
				foreach (var (name, text) in files)
				{
					using var entry = zip.CreateEntry(name).Open();
					var bytes = Encoding.UTF8.GetBytes(text);
					entry.Write(bytes, 0, bytes.Length);
				}
			return stream.ToArray();
		}

		private static Dictionary<string, string> Read(byte[] packet)
		{
			using var zip = new ZipArchive(new MemoryStream(packet), ZipArchiveMode.Read);
			return zip.Entries.ToDictionary(e => e.FullName, e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); });
		}
	}
}
