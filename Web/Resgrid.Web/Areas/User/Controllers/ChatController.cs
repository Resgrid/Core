using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize]
	public class ChatController : SecureBaseController
	{
		/// <summary>
		/// Full-page Slack-style chat workspace, rendered by the &lt;rg-chat-page&gt; React element.
		/// </summary>
		[HttpGet]
		public IActionResult Index()
		{
			return View();
		}

		/// <summary>
		/// AI assistant conversation, rendered by the &lt;rg-chatbot&gt; React element.
		/// </summary>
		[HttpGet]
		public IActionResult Chatbot()
		{
			return View();
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
