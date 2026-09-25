using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
			var entry = catalog.Settings.FirstOrDefault(s => s.Location.Controller == controller && s.Location.Action == action && s.Location.Field == For.Name);
			if (entry == null) return;
			var http = ViewContext.HttpContext;
			const string key = "AdminAssist.FieldHelpAccess";
			if (!http.Items.TryGetValue(key, out var cached))
			{
				var actor = new AdminAssistActor(ClaimsAuthorizationHelper.GetDepartmentId(), ClaimsAuthorizationHelper.GetUserId(), CultureInfo.CurrentUICulture.Name);
				cached = await access.CanAccessAsync(actor, true, http.RequestAborted) || await access.CanAccessAsync(actor, false, http.RequestAborted);
				http.Items[key] = cached;
			}
			if (cached is not true) return;
			output.Attributes.SetAttribute("data-aa-field", entry.Id);
			var helpId = "aa-help-" + entry.Id;
			var existing = output.Attributes["aria-describedby"]?.Value?.ToString();
			output.Attributes.SetAttribute("aria-describedby", string.IsNullOrEmpty(existing) ? helpId : existing + " " + helpId);
			var help = new TagBuilder("span"); help.Attributes["id"] = helpId; help.AddCssClass("help-block admin-assist-field-help");
			help.InnerHtml.Append(labels[entry.HelpKey]); output.PostElement.AppendHtml(help);
			if (string.Equals(http.Request.Query["aa"], For.Name, StringComparison.Ordinal))
			{
				output.Attributes.SetAttribute("autofocus", "autofocus");
				output.Attributes.SetAttribute("data-aa-highlight", "true");
				output.Attributes.SetAttribute("class", (output.Attributes["class"]?.Value?.ToString() + " admin-assist-field-highlight").Trim());
			}
		}
	}
}
