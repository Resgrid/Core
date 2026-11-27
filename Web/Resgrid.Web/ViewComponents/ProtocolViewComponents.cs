using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.ViewComponents
{
	/// <summary>What the document content partial shows: the version's file, its extracted text, and where it opens.</summary>
	public sealed class ProtocolDocumentContentModel
	{
		public int VersionId { get; set; }
		public string Title { get; set; }
		public ProtocolDocumentView Document { get; set; }
		/// <summary>Draft and review pages show the upload hint instead of the reading copy.</summary>
		public bool ShowUpload { get; set; }
		public int ProtocolId { get; set; }
	}

	/// <summary>
	/// The reading view of a document-backed version (P1 R-P5): the PDF inline in the browser's viewer with a download link (a DOCX
	/// downloads), and the extracted text below it for search and accessibility. The file itself streams from
	/// ProtocolDocuments/Document behind the per-version read rule; this component only renders metadata and text.
	/// </summary>
	public sealed class ProtocolDocumentContentViewComponent(IProtocolDocumentService documents) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(int versionId, string title, int protocolId = 0, bool showUpload = false)
		{
			try
			{
				var departmentId = ClaimsAuthorizationHelper.GetDepartmentId();
				var view = await documents.GetDocumentViewAsync(departmentId, versionId);
				return View("~/Areas/User/Views/Protocols/_DocumentContent.cshtml", new ProtocolDocumentContentModel
				{
					VersionId = versionId,
					Title = title,
					Document = view,
					ShowUpload = showUpload,
					ProtocolId = protocolId
				});
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Document content of protocol version {versionId} could not be shown.");
				return Content(string.Empty);
			}
		}
	}

	/// <summary>
	/// On a protocol's reading page: the member's outstanding acknowledgment of it, with the way to read and sign (P2). Nothing when they
	/// owe none.
	/// </summary>
	public sealed class ProtocolAcknowledgmentPromptViewComponent(IProtocolAcknowledgmentService acknowledgments) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync(int protocolId)
		{
			try
			{
				var items = await acknowledgments.GetMyAcknowledgmentsAsync(ClaimsAuthorizationHelper.GetDepartmentId(), ClaimsAuthorizationHelper.GetUserId(), false, DateTime.UtcNow);
				var owed = items.FirstOrDefault(i => i.ProtocolId == protocolId && ProtocolAcknowledgmentRules.IsOutstanding(i.Acknowledgment));
				return owed == null ? Content(string.Empty) : View("~/Areas/User/Views/Protocols/_AcknowledgmentPrompt.cshtml", owed);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Acknowledgment prompt for protocol {protocolId} could not be shown.");
				return Content(string.Empty);
			}
		}
	}

	/// <summary>
	/// The must-read card at the top of home (R-P7): every priority bulletin in force the member has not acknowledged yet. Rendered on
	/// the home dashboard only, never on the dispatch console, a call or a status change (a guard test pins where it is used).
	/// </summary>
	public sealed class ProtocolMustReadViewComponent(IProtocolAcknowledgmentService acknowledgments) : ViewComponent
	{
		public async Task<IViewComponentResult> InvokeAsync()
		{
			try
			{
				var items = await acknowledgments.GetMustReadAsync(ClaimsAuthorizationHelper.GetDepartmentId(), ClaimsAuthorizationHelper.GetUserId(), DateTime.UtcNow);
				return items.Count == 0 ? Content(string.Empty) : View("~/Areas/User/Views/Shared/_ProtocolMustRead.cshtml", items);
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				// Optional guidance must not take down the operational dashboard.
				Logging.LogException(ex, "Must-read bulletins could not be shown.");
				return Content(string.Empty);
			}
		}
	}
}
