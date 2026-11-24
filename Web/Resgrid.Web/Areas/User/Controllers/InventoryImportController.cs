using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Models.Inventory;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Inventory → Import: upload a spreadsheet of items and on-hand counts, review the plan, apply it. Every step is a
	/// full-page POST (never a link) so the Advanced Data Protection grant rides along, as on the other inventory screens.
	/// Refusals render on the page instead of the JSON error the command endpoints return.
	/// </summary>
	public sealed partial class InventoryController
	{
		private IInventoryImportService Imports => _imports ?? throw new InventoryException(409, "OperationUnavailable");

		private async Task<InventoryImportView> ImportViewAsync()
		{
			var view = new InventoryImportView { Migrated = await _migration.IsMigratedAsync(DepartmentId) };
			var groupId = (await _groups.GetGroupForUserAsync(UserId, DepartmentId))?.DepartmentGroupId;
			view.CanWrite = await MayWriteAsync(PermissionTypes.AdjustInventory, groupId);
			return view;
		}

		private static void Refuse(InventoryImportView view, InventoryException error)
		{
			view.ErrorCode = error.Code;
			view.Locked = error.Code == "ProtectedDataRequired";
		}

		private FileContentResult ImportFile(string text, string name)
		{
			Response.Headers["Cache-Control"] = "no-store";
			return File(InventoryImportCsv.Encode(text), "text/csv; charset=utf-8", name);
		}

		private async Task PreviewIntoAsync(InventoryImportView view, string text, string requestId)
		{
			var input = InventoryImportCsv.Parse(text, requestId, CultureInfo.CurrentCulture);
			view.Csv = text; view.RequestId = input.RequestId;
			view.Preview = await Imports.PreviewImportAsync(Actor, input);
		}

		[HttpGet]
		public async Task<IActionResult> Import() => View("Import", await ImportViewAsync());

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ReopenImport() => View("Import", await ImportViewAsync());

		[HttpGet]
		public IActionResult ImportTemplate() => ImportFile(InventoryImportCsv.Template(), "inventory-import-template.csv");

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ExportImport()
		{
			try
			{
				return ImportFile(await Imports.ExportImportFileAsync(Actor), "inventory-" + DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv");
			}
			catch (InventoryException error)
			{
				var view = await ImportViewAsync(); Refuse(view, error);
				return View("Import", view);
			}
		}

		// The file plus multipart framing must fit; the controller's 1 MB command limit is for the JSON command forms.
		[HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(InventoryImportLimits.MaxFileBytes * 2)]
		public async Task<IActionResult> PreviewImport(IFormFile file, string requestId)
		{
			var view = await ImportViewAsync();
			try
			{
				if (file == null || file.Length == 0) throw new InventoryException(400, "ImportFileRequired");
				if (file.Length > InventoryImportLimits.MaxFileBytes) throw new InventoryException(400, "ImportFileTooLarge");
				byte[] bytes;
				using (var buffer = new MemoryStream())
				{
					// Reading the upload stops when the request does; nothing has been written yet.
					await file.CopyToAsync(buffer, HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None);
					bytes = buffer.ToArray();
				}
				view.FileName = Path.GetFileName(file.FileName);
				var text = InventoryImportCsv.Decode(bytes) ?? throw new InventoryException(400, "ImportFileRequired");
				await PreviewIntoAsync(view, text, Guid.TryParseExact(requestId, "D", out var id) && id != Guid.Empty ? requestId : Guid.NewGuid().ToString("D"));
			}
			catch (InventoryException error) { Refuse(view, error); }
			return View("Import", view);
		}

		/// <summary>
		/// Applies the reviewed file. The file travels back as text with the preview's hash; the service re-plans it and refuses
		/// if anything moved since. A refusal shows the fresh preview, so the person sees what changed and can apply again.
		/// </summary>
		[HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(InventoryImportLimits.MaxFileBytes * 3)]
		public async Task<IActionResult> ApplyImport(string data, string requestId, string previewHash, string fileName)
		{
			var view = await ImportViewAsync();
			view.FileName = fileName == null ? null : Path.GetFileName(fileName);
			string csv = null;
			try
			{
				try { csv = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(data ?? "")); }
				catch (FormatException) { throw new InventoryException(400, "ImportFileInvalid"); }
				var input = InventoryImportCsv.Parse(csv, requestId, CultureInfo.CurrentCulture);
				input.PreviewHash = previewHash;
				view.Result = await Imports.ApplyImportAsync(Actor, input);
			}
			catch (InventoryException error)
			{
				Refuse(view, error);
				if (!view.Locked && !string.IsNullOrWhiteSpace(csv))
				{
					try { await PreviewIntoAsync(view, csv, requestId); }
					catch (InventoryException again) { if (again.Code == "ProtectedDataRequired") view.Locked = true; view.Preview = null; }
				}
			}
			return View("Import", view);
		}
	}
}
