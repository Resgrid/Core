using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Inventory;
using Resgrid.Web.Helpers;
using InventoryStrings = Resgrid.Localization.Areas.User.Inventory.Inventory;

namespace Resgrid.Tests.Web.User
{
	/// <summary>Inventory → Import screens: refusals render on the page, a stale apply shows the fresh preview.</summary>
	[TestFixture, NonParallelizable]
	public sealed class InventoryImportControllerTests
	{
		private const int DepartmentId = 77;
		private const string UserId = "inventory-member";
		private Mock<IInventoryImportService> _imports;
		private InventoryController _controller;
		private IHttpContextAccessor _previousAccessor;

		[SetUp]
		public void SetUp()
		{
			_imports = new Mock<IInventoryImportService>();
			var authorization = new Mock<IInventoryAuthorizationService>();
			authorization.Setup(s => s.RequireAsync(It.IsAny<InventoryActor>(), It.IsAny<bool>(), It.IsAny<PermissionTypes?>(), It.IsAny<int?>())).Returns(Task.CompletedTask);
			var migration = new Mock<IInventoryMigrationService>(); migration.Setup(s => s.IsMigratedAsync(DepartmentId)).ReturnsAsync(true);
			var groups = new Mock<IDepartmentGroupsService>();
			var strings = new Mock<IStringLocalizer<InventoryStrings>>(); strings.Setup(s => s[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()) }, "Test")) };
			http.Request.Method = "POST";
			var accessor = new HttpContextAccessor { HttpContext = http }; _previousAccessor = ClaimsAuthorizationHelper._httpContextAccessor; ClaimsAuthorizationHelper._httpContextAccessor = accessor;
			_controller = new InventoryController(Mock.Of<IInventoryCatalogService>(), Mock.Of<IInventoryStockService>(), Mock.Of<IInventoryTransferService>(), Mock.Of<IInventoryIssuanceService>(),
				migration.Object, authorization.Object, new HttpProtectedGrantContext(accessor), Mock.Of<IDepartmentDataProtectionService>(), Mock.Of<IUnitsService>(), groups.Object,
				Mock.Of<IDepartmentsService>(), Mock.Of<IWorkOrderMaintenanceRepository>(), strings.Object, imports: _imports.Object)
			{ ControllerContext = new ControllerContext { HttpContext = http } };
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = _previousAccessor;

		private static InventoryImportView Model(IActionResult result) => (InventoryImportView)((ViewResult)result).Model;

		private static IFormFile Upload(string text)
		{
			var bytes = Encoding.UTF8.GetBytes(text);
			return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "counts.csv");
		}

		[Test]
		public async Task Preview_without_a_file_says_so_on_the_page()
		{
			var view = Model(await _controller.PreviewImport(null, null));

			view.ErrorCode.Should().Be("ImportFileRequired"); view.Preview.Should().BeNull(); view.CanWrite.Should().BeTrue();
			_imports.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Preview_reads_the_upload_and_keeps_the_request_id()
		{
			var requestId = Guid.NewGuid().ToString("D");
			_imports.Setup(s => s.PreviewImportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryImportInput>())).ReturnsAsync(new InventoryImportPreview { PreviewHash = "hash" });

			var view = Model(await _controller.PreviewImport(Upload("Item,Qty\r\nGauze,4\r\n"), requestId));

			view.Preview.PreviewHash.Should().Be("hash"); view.RequestId.Should().Be(requestId); view.FileName.Should().Be("counts.csv");
			view.Csv.Should().Contain("Gauze,4");
			_imports.Verify(s => s.PreviewImportAsync(It.Is<InventoryActor>(a => a.DepartmentId == DepartmentId && a.UserId == UserId),
				It.Is<InventoryImportInput>(i => i.RequestId == requestId && i.Rows.Single().Item == "Gauze" && i.Rows.Single().Quantity == 4)), Times.Once);
		}

		[Test]
		public async Task A_stale_apply_shows_the_fresh_preview()
		{
			var csv = "Item,Quantity\r\nGauze,4\r\n"; var requestId = Guid.NewGuid().ToString("D");
			_imports.Setup(s => s.ApplyImportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryImportInput>())).ThrowsAsync(new InventoryException(409, "ImportPreviewChanged"));
			_imports.Setup(s => s.PreviewImportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryImportInput>())).ReturnsAsync(new InventoryImportPreview { PreviewHash = "fresh" });

			var view = Model(await _controller.ApplyImport(Convert.ToBase64String(Encoding.UTF8.GetBytes(csv)), requestId, "stale", "counts.csv"));

			view.ErrorCode.Should().Be("ImportPreviewChanged"); view.Preview.PreviewHash.Should().Be("fresh"); view.Result.Should().BeNull();
			view.Csv.Should().Be(csv); view.RequestId.Should().Be(requestId);
			_imports.Verify(s => s.ApplyImportAsync(It.IsAny<InventoryActor>(), It.Is<InventoryImportInput>(i => i.PreviewHash == "stale" && i.RequestId == requestId)), Times.Once);
		}

		[Test]
		public async Task Applying_shows_the_result()
		{
			_imports.Setup(s => s.ApplyImportAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryImportInput>()))
				.ReturnsAsync(new InventoryImportResult { OperationId = "op", Totals = new InventoryImportTotals { ItemsCreated = 1 }, TransactionCount = 1 });

			var view = Model(await _controller.ApplyImport(Convert.ToBase64String(Encoding.UTF8.GetBytes("Item,Quantity\r\nGauze,4\r\n")), Guid.NewGuid().ToString("D"), "hash", null));

			view.Result.Totals.ItemsCreated.Should().Be(1); view.ErrorCode.Should().BeNull();
		}

		[Test]
		public async Task A_tampered_payload_is_refused_without_reaching_the_service()
		{
			var view = Model(await _controller.ApplyImport("%%% not base64 %%%", Guid.NewGuid().ToString("D"), "hash", null));

			view.ErrorCode.Should().Be("ImportFileInvalid");
			_imports.VerifyNoOtherCalls();
		}

		[Test]
		public void The_template_downloads_as_utf8_csv_with_a_byte_order_mark()
		{
			var file = (FileContentResult)_controller.ImportTemplate();

			file.ContentType.Should().Be("text/csv; charset=utf-8"); file.FileDownloadName.Should().Be("inventory-import-template.csv");
			file.FileContents.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
			Encoding.UTF8.GetString(file.FileContents, 3, file.FileContents.Length - 3).Should().Be(InventoryImportCsv.Template());
		}
	}
}
