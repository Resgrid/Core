using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize]
	public class ChatController : SecureBaseController
	{
		private readonly IFeatureToggleService _featureToggleService;

		public ChatController(IFeatureToggleService featureToggleService)
		{
			_featureToggleService = featureToggleService;
		}

		private Task<bool> ChatEnabledAsync()
		{
			return _featureToggleService.IsEnabledAsync(FeatureFlagKeys.ChatSystem, DepartmentId);
		}

		/// <summary>
		/// Full-page Slack-style chat workspace, rendered by the &lt;rg-chat-page&gt; React element.
		/// </summary>
		[HttpGet]
		public async Task<IActionResult> Index()
		{
			if (!await ChatEnabledAsync())
				return RedirectToAction("Dashboard", "Home", new { Area = "User" });

			return View();
		}

		/// <summary>
		/// The assistant is no longer a standalone page: it lives in the &lt;rg-assistant&gt; footer
		/// slide-out on every page. Old links land on the chat workspace.
		/// </summary>
		[HttpGet]
		public IActionResult Chatbot()
		{
			return RedirectToAction("Index");
		}

		/// <summary>
		/// Chat moderation console (flags, actions, settings, exports), rendered by the
		/// &lt;rg-chat-moderation&gt; React element. Department and group administrators.
		/// </summary>
		[HttpGet]
		public IActionResult Moderation()
		{
			return RedirectToAction("Index", "Moderation", new { Area = "User" });
		}
	}
}
