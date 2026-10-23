using System;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using Resgrid.Framework;

namespace Resgrid.Web
{
	public static class HtmlHelpers
	{
		/// <summary>
		/// Stored rich text (call nature and notes, messages, notes, trainings, protocols, documents, calendar items, logs...)
		/// written as markup after the allow-list sanitizer has removed scripts, handlers, styles, frames and script URLs.
		/// Use it instead of Html.Raw for any user-authored HTML: values reach the database from the v4 API, inbound e-mail,
		/// imports and older code paths that never sanitized, so the save path cannot be relied on.
		/// </summary>
		public static IHtmlContent SanitizedHtml(this IHtmlHelper html, string value)
		{
			var clean = StringHelpers.SanitizeHtmlForDisplay(value);
			return clean.Length == 0 ? HtmlString.Empty : new HtmlString(clean);
		}

		/// <summary>
		/// Newtonsoft settings for JSON written into a page (a &lt;script&gt; block, a data attribute or an inline handler):
		/// &lt;, &gt;, &amp; and quotes are \u-escaped, so a name such as "&lt;/script&gt;&lt;script&gt;..." stays inside its string
		/// instead of closing the block. The JSON value itself is unchanged.
		/// </summary>
		public static readonly JsonSerializerSettings ScriptJsonSettings = new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeHtml };

		/// <summary>The value as JSON that is safe to write into a page; see <see cref="ScriptJsonSettings"/>.</summary>
		public static IHtmlContent ScriptJson(this IHtmlHelper html, object value) => new HtmlString(ScriptJsonString(value));

		/// <summary>The value as JSON that is safe to write into a page with Html.Raw; see <see cref="ScriptJsonSettings"/>.</summary>
		public static string ScriptJsonString(object value) => JsonConvert.SerializeObject(value, ScriptJsonSettings);

		public static string IsSelected(this IHtmlHelper html, string controller = null, string action = null, string cssClass = null)
		{
			if (String.IsNullOrEmpty(cssClass))
				cssClass = "active";

			string currentAction = (string)html.ViewContext.RouteData.Values["action"];
			string currentController = (string)html.ViewContext.RouteData.Values["controller"];

			if (String.IsNullOrEmpty(controller))
				controller = currentController;

			if (String.IsNullOrEmpty(action))
				action = currentAction;

			return controller == currentController && action == currentAction ?
					cssClass : String.Empty;
		}

		/// <summary>
		/// True when the request is being handled by any of the named controllers. Used by the
		/// sidebar to light up and expand a group when the user is on one of its pages, including
		/// the pages inside the group that have no link of their own.
		/// </summary>
		public static bool IsController(this IHtmlHelper html, params string[] controllers)
		{
			string currentController = (string)html.ViewContext.RouteData.Values["controller"];

			foreach (var controller in controllers)
				if (String.Equals(controller, currentController, StringComparison.OrdinalIgnoreCase))
					return true;

			return false;
		}

		public static string PageClass(this IHtmlHelper htmlHelper)
		{
			string currentAction = (string) htmlHelper.ViewContext.RouteData.Values["action"];
			return currentAction;
		}
	}
}