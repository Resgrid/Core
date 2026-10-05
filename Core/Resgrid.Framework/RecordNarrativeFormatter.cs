using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Resgrid.Framework
{
	/// <summary>Records permit basic narrative formatting, without links, media, attributes or active content.</summary>
	public static class RecordNarrativeFormatter
	{
		private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "p", "br", "strong", "b", "em", "i", "u", "ul", "ol", "li", "blockquote", "h1", "h2", "h3" };
		private static readonly HashSet<string> Drop = new(StringComparer.OrdinalIgnoreCase) { "script", "style", "iframe", "object", "embed", "svg", "math", "template", "noscript" };
		private static bool IsHtml(string value) => Regex.IsMatch(value ?? "", @"<\s*/?[a-zA-Z][^>]*>", RegexOptions.None, TimeSpan.FromMilliseconds(250));
		public static string ForStorage(string value) => IsHtml(value) ? Render(value) : value;
		public static string Render(string value)
		{
			if (!IsHtml(value)) return WebUtility.HtmlEncode(value ?? "");
			var document = new HtmlDocument(); document.LoadHtml(value); var html = new StringBuilder();
			void Append(HtmlNode node, int depth)
			{
				if (depth > 64) throw new ArgumentException("Narrative formatting is nested too deeply.");
				if (node.NodeType == HtmlNodeType.Text) { html.Append(WebUtility.HtmlEncode(HtmlEntity.DeEntitize(node.InnerText))); return; }
				if (node.NodeType == HtmlNodeType.Comment || Drop.Contains(node.Name)) return;
				var allowed = Allowed.Contains(node.Name);
				if (allowed) html.Append('<').Append(node.Name).Append('>');
				foreach (var child in node.ChildNodes) Append(child, depth + 1);
				if (allowed && node.Name != "br") html.Append("</").Append(node.Name).Append('>');
			}
			Append(document.DocumentNode, 0); return html.ToString();
		}
		/// <summary>The narrative as plain text (paragraphs and list items on their own lines), for copying into another report.</summary>
		public static string ToPlainText(string value)
		{
			if (string.IsNullOrWhiteSpace(value)) return string.Empty;
			if (!IsHtml(value)) return value.Trim();
			var document = new HtmlDocument(); document.LoadHtml(Render(value)); var text = new StringBuilder();
			void Append(HtmlNode node, int depth)
			{
				if (depth > 64) return;
				if (node.NodeType == HtmlNodeType.Text) { text.Append(HtmlEntity.DeEntitize(node.InnerText)); return; }
				if (node.Name == "br") { text.Append('\n'); return; }
				if (node.Name == "li") text.Append("- ");
				foreach (var child in node.ChildNodes) Append(child, depth + 1);
				if (node.Name is "p" or "li" or "h1" or "h2" or "h3" or "blockquote") text.Append('\n');
			}
			Append(document.DocumentNode, 0);
			return Regex.Replace(text.ToString().Replace('​', ' '), @"\n{3,}", "\n\n", RegexOptions.None, TimeSpan.FromMilliseconds(250)).Trim();
		}
		public static bool HasText(string value)
		{
			var document = new HtmlDocument(); document.LoadHtml(Render(value));
			return !string.IsNullOrWhiteSpace(HtmlEntity.DeEntitize(document.DocumentNode.InnerText).Replace('\u200b', ' '));
		}
	}
}
