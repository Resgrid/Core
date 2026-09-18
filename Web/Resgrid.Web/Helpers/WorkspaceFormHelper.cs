using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Field builders for the module workspace screens (inventory, checklists). Every control
	/// comes out as a labelled Bootstrap form group with optional help text underneath, so the
	/// screens stay consistent with each other and the views stay readable.
	/// </summary>
	public static class WorkspaceFormHelper
	{
		private const string EmptyChoice = "—";

		/// <summary>Renders one labelled field: input, select, textarea or checkbox.</summary>
		/// <param name="localizer">Inventory string localizer; <paramref name="label"/> and <paramref name="help"/> are resource keys.</param>
		/// <param name="wide">Span the full width of an <c>.rgw-grid</c> rather than one column.</param>
		public static IHtmlContent Field(IStringLocalizer localizer, string name, string label, object value = null,
			string type = "text", IEnumerable<SelectListItem> options = null, string help = null,
			bool required = false, bool readOnly = false, bool wide = false, int maxLength = 16000,
			string step = null, string min = null, string max = null, string ariaLabel = null)
		{
			var identity = "rgw-" + Guid.NewGuid().ToString("N");
			var caption = localizer[label].Value;
			var text = value is DateTime date
				? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				: Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

			var group = new TagBuilder("div");
			group.AddCssClass("form-group");
			if (wide)
				group.AddCssClass("rgw-wide");

			if (type == "checkbox")
			{
				var box = new TagBuilder("input") { TagRenderMode = TagRenderMode.SelfClosing };
				box.Attributes["type"] = "checkbox";
				box.Attributes["name"] = name;
				box.Attributes["id"] = identity;
				box.Attributes["value"] = "true";
				if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
					box.Attributes["checked"] = "checked";

				var boxLabel = new TagBuilder("label");
				boxLabel.Attributes["for"] = identity;
				boxLabel.InnerHtml.AppendHtml(box);
				boxLabel.InnerHtml.Append(" " + caption);

				var wrap = new TagBuilder("div");
				wrap.AddCssClass("checkbox");
				wrap.InnerHtml.AppendHtml(boxLabel);
				group.InnerHtml.AppendHtml(wrap);

				// Paired hidden field so clearing the box posts an explicit false; without it
				// the model binder leaves the property at whatever the input class defaults to.
				group.InnerHtml.AppendHtml(Hidden(name, "false"));

				AppendHelp(group, localizer, help);
				return group;
			}

			var title = new TagBuilder("label");
			title.Attributes["for"] = identity;
			title.InnerHtml.Append(caption);
			group.InnerHtml.AppendHtml(title);

			TagBuilder element;
			if (options != null)
			{
				element = new TagBuilder("select");
				foreach (var option in options)
				{
					var item = new TagBuilder("option");
					item.Attributes["value"] = option.Value ?? string.Empty;
					if ((option.Value ?? string.Empty) == text)
						item.Attributes["selected"] = "selected";
					item.InnerHtml.Append(option.Text);
					element.InnerHtml.AppendHtml(item);
				}
			}
			else if (type == "textarea")
			{
				element = new TagBuilder("textarea");
				element.Attributes["rows"] = "3";
				element.Attributes["maxlength"] = maxLength.ToString(CultureInfo.InvariantCulture);
				element.InnerHtml.Append(text);
			}
			else
			{
				element = new TagBuilder("input") { TagRenderMode = TagRenderMode.SelfClosing };
				element.Attributes["type"] = type;
				element.Attributes["value"] = text;
				element.Attributes["maxlength"] = maxLength.ToString(CultureInfo.InvariantCulture);
				if (type == "number")
					element.Attributes["step"] = step ?? "0.000001";
				if (min != null)
					element.Attributes["min"] = min;
				if (max != null)
					element.Attributes["max"] = max;
			}

			element.AddCssClass("form-control");
			element.Attributes["name"] = name;
			element.Attributes["id"] = identity;
			if (required)
				element.Attributes["required"] = "required";
			if (readOnly)
				element.Attributes["readonly"] = "readonly";
			if (!string.IsNullOrEmpty(ariaLabel))
				element.Attributes["aria-label"] = ariaLabel;
			group.InnerHtml.AppendHtml(element);

			AppendHelp(group, localizer, help);
			return group;
		}

		/// <summary>
		/// A hidden field, for values the screen carries but never asks about. Written by hand
		/// rather than with a TagBuilder because TagBuilder sorts attributes alphabetically, and
		/// the page state these fields carry is asserted on by name/value pairs in the tests.
		/// </summary>
		public static IHtmlContent Hidden(string name, object value)
		{
			var text = value is DateTime date
				? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				: Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
			return new HtmlString("<input type=\"hidden\" name=\"" + HtmlEncoder.Default.Encode(name) +
				"\" value=\"" + HtmlEncoder.Default.Encode(text) + "\" />");
		}

		/// <summary>Choice list with a leading blank entry, for optional selects.</summary>
		public static IEnumerable<SelectListItem> Choices(IEnumerable<(string Id, string Name)> choices) =>
			new[] { new SelectListItem { Value = string.Empty, Text = EmptyChoice } }
				.Concat(choices.Select(x => new SelectListItem { Value = x.Id, Text = x.Name }));

		/// <summary>Choice list without a blank entry, for selects that must hold a value.</summary>
		public static IEnumerable<SelectListItem> Required(IEnumerable<(string Id, string Name)> choices) =>
			choices.Select(x => new SelectListItem { Value = x.Id, Text = x.Name });

		/// <summary>Enum codes rendered from "{prefix}{value}" resource keys, such as Movement3.</summary>
		public static IEnumerable<SelectListItem> Codes(IStringLocalizer localizer, string prefix, params int[] values) =>
			values.Select(x => new SelectListItem { Value = x.ToString(CultureInfo.InvariantCulture), Text = localizer[prefix + x].Value });

		/// <summary>Yes/No pair for the few flags that stay a dropdown rather than a checkbox.</summary>
		public static IEnumerable<SelectListItem> YesNo(IStringLocalizer localizer) => new[]
		{
			new SelectListItem { Value = "false", Text = localizer["No"].Value },
			new SelectListItem { Value = "true", Text = localizer["Yes"].Value }
		};

		private static void AppendHelp(TagBuilder group, IStringLocalizer localizer, string help)
		{
			if (string.IsNullOrEmpty(help))
				return;

			var resolved = localizer[help];
			if (resolved.ResourceNotFound)
				return;

			var block = new TagBuilder("span");
			block.AddCssClass("help-block");
			block.InnerHtml.Append(resolved.Value);
			group.InnerHtml.AppendHtml(block);
		}
	}
}
