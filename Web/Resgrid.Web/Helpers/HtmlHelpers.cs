using System;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Resgrid.Web
{
	public static class HtmlHelpers
	{

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