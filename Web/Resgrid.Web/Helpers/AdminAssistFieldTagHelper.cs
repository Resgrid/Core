using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Helpers;

namespace Resgrid.Web.Helpers
{
	/// <summary>Public, release-pinned help attached to an existing editor. Does not read or submit its value.</summary>
	[HtmlTargetElement("input", Attributes = "asp-for")]
	[HtmlTargetElement("select", Attributes = "asp-for")]
	[HtmlTargetElement("textarea", Attributes = "asp-for")]
	public sealed class AdminAssistFieldTagHelper(IAdminAssistCatalog catalog, IAdminAssistAccessService access,
		IStringLocalizer<Resgrid.Localization.Areas.User.AdminAssist.AdminAssist> labels) : TagHelper
	{
		[HtmlAttributeName("asp-for")] public ModelExpression For { get; set; }
		[HtmlAttributeNotBound, ViewContext] public ViewContext ViewContext { get; set; }
		public override int Order => 1000;
		public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
		{
			if (For == null || ViewContext == null || output.Attributes["type"]?.Value?.ToString() == "hidden") return;
			var controller = ViewContext.RouteData.Values["controller"]?.ToString();
			var action = ViewContext.RouteData.Values["action"]?.ToString();
			var entry = catalog.FieldHelpEntry(controller, action, For.Name);
			if (entry == null) return;
			var http = ViewContext.HttpContext;
			const string key = "AdminAssist.FieldHelpAccess";
			if (!http.Items.TryGetValue(key, out var cached))
			{
				var actor = new AdminAssistActor(ClaimsAuthorizationHelper.GetDepartmentId(), ClaimsAuthorizationHelper.GetUserId(), CultureInfo.CurrentUICulture.Name);
				try
				{
					cached = await access.CanAccessAsync(actor, true, http.RequestAborted) || await access.CanAccessAsync(actor, false, http.RequestAborted);
				}
				catch (OperationCanceledException) { throw; }
				catch (Exception) { cached = false; } // Optional help never blocks the owning editor; cached so later fields skip the check.
				http.Items[key] = cached;
			}
			if (cached is not true) return;
			output.Attributes.SetAttribute("data-aa-field", entry.Id);
			var helpId = "aa-help-" + entry.Id;
			var existing = output.Attributes["aria-describedby"]?.Value?.ToString();
			output.Attributes.SetAttribute("aria-describedby", string.IsNullOrEmpty(existing) ? helpId : existing + " " + helpId);
			var help = new TagBuilder("span"); help.Attributes["id"] = helpId; help.AddCssClass("help-block admin-assist-field-help");
			help.InnerHtml.Append(labels[entry.HelpKey]);
			// Inside <admin-assist-help-after> the help goes after that block, not between the editor and the label or switch it drives.
			if (context.Items.TryGetValue(typeof(AdminAssistHelpAfterTagHelper), out var after) && after is HtmlContentBuilder deferred) deferred.AppendHtml(help);
			else output.PostElement.AppendHtml(help);
			if (string.Equals(http.Request.Query["aa"], For.Name, StringComparison.Ordinal))
			{
				output.Attributes.SetAttribute("autofocus", "autofocus");
				output.Attributes.SetAttribute("data-aa-highlight", "true");
				output.Attributes.SetAttribute("class", (output.Attributes["class"]?.Value?.ToString() + " admin-assist-field-highlight").Trim());
			}
		}
	}

	/// <summary>
	/// Renders its content unchanged, then the Admin Assist field help of the editors inside it. Wrap an editor whose next
	/// sibling is part of the control (an on/off switch's label, a styled checkbox's label, a label wrapping the checkbox and
	/// its caption): help appended straight after the editor would land between them, breaking the "input + label" styling.
	/// Wrap an editor in a narrow column too, with <c>help-class</c> naming the full-width column the help goes in instead
	/// (e.g. "col-sm-offset-3 col-sm-9" under a col-sm-3 label), or the help wraps a word or two per line.
	/// </summary>
	[HtmlTargetElement("admin-assist-help-after")]
	public sealed class AdminAssistHelpAfterTagHelper : TagHelper
	{
		private readonly HtmlContentBuilder _help = new();
		[HtmlAttributeName("help-class")] public string HelpClass { get; set; }
		public override void Init(TagHelperContext context) => context.Items[typeof(AdminAssistHelpAfterTagHelper)] = _help;
		public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
		{
			output.TagName = null;
			output.Content.SetHtmlContent(await output.GetChildContentAsync());
			if (_help.Count == 0) return;
			if (string.IsNullOrEmpty(HelpClass)) { output.PostContent.AppendHtml(_help); return; }
			var column = new TagBuilder("div"); column.AddCssClass(HelpClass); column.InnerHtml.AppendHtml(_help);
			output.PostContent.AppendHtml(column);
		}
	}
}
