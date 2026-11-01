using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Planning
{
	/// <summary>
	/// links.quick (plan §7.6). Department links are the Department Links page's "View data" rows — enabled links where
	/// another department shares with this one — and only when the plan allows links (CanDepartmentUseLinksAsync). The
	/// member's own links come from the "links" text setting, one per line as "Label | https://…"; only absolute http and
	/// https URLs survive (javascript:, data:, relative and malformed lines are dropped and counted).
	/// </summary>
	[DashboardWidget("links.quick")]
	public class QuickLinksWidgetProvider : IDashboardWidgetDataProvider
	{
		public const int MaxLinksSettingLength = 2000;
		public const int MaxUserLinks = 20;
		public const int MaxLabelLength = 100;

		/// <summary>"[label |] http(s)://…" — the URL is the line's last token, so a label may itself contain "|".</summary>
		private static readonly Regex LinePattern = new Regex(@"^(?:(?<label>.*?)\s*\|\s*)?(?<url>https?://\S+)$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

		private readonly IDepartmentLinksService _departmentLinksService;
		private readonly ILimitsService _limitsService;

		public QuickLinksWidgetProvider(IDepartmentLinksService departmentLinksService, ILimitsService limitsService)
		{
			_departmentLinksService = departmentLinksService;
			_limitsService = limitsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var data = new QuickLinksWidgetData();
			var (links, invalid) = ParseUserLinks(context.GetString("links"));
			data.UserLinks = links;
			data.InvalidUserLinks = invalid;

			if (!context.GetBool("showDepartmentLinks", true))
				return data;

			data.DepartmentLinksAvailable = await _limitsService.CanDepartmentUseLinksAsync(context.DepartmentId);
			if (!data.DepartmentLinksAvailable)
				return data;

			data.DepartmentLinks = (await _departmentLinksService.GetAllLinksForDepartmentAsync(context.DepartmentId) ?? new List<DepartmentLink>())
				.Where(l => l != null && l.LinkEnabled && l.LinkedDepartmentId == context.DepartmentId && l.DepartmentId != context.DepartmentId)
				.OrderBy(l => l.Department?.Name, StringComparer.OrdinalIgnoreCase)
				.Select(l => new QuickLinkDepartment
				{
					DepartmentLinkId = l.DepartmentLinkId,
					DepartmentName = l.Department?.Name ?? string.Empty,
					Color = l.DepartmentColor,
					SharesCalls = l.DepartmentShareCalls,
					SharesUnits = l.DepartmentShareUnits,
					SharesPersonnel = l.DepartmentSharePersonnel,
					Url = "/User/Links/View?linkId=" + l.DepartmentLinkId.ToString(CultureInfo.InvariantCulture)
				})
				.ToList();

			return data;
		}

		/// <summary>"Label | url" per line (a bare URL labels itself with its host). Returns the valid links and how many lines were dropped.</summary>
		public static (List<QuickLinkItem> Links, int Invalid) ParseUserLinks(string setting)
		{
			var links = new List<QuickLinkItem>();
			var invalid = 0;

			if (string.IsNullOrWhiteSpace(setting))
				return (links, invalid);

			foreach (var raw in setting.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var line = raw.Trim();
				if (line.Length == 0)
					continue;

				if (links.Count >= MaxUserLinks)
				{
					invalid++;
					continue;
				}

				Match match;
				try
				{
					match = LinePattern.Match(line);
				}
				catch (RegexMatchTimeoutException)
				{
					match = Match.Empty;
				}

				var label = match.Success ? match.Groups["label"].Value.Trim() : null;

				if (!match.Success || !TryNormalizeUrl(match.Groups["url"].Value, out var url))
				{
					invalid++;
					continue;
				}

				if (string.IsNullOrWhiteSpace(label))
					label = url.Host;

				links.Add(new QuickLinkItem
				{
					Label = label.Length > MaxLabelLength ? label.Substring(0, MaxLabelLength) : label,
					Url = url.AbsoluteUri
				});
			}

			return (links, invalid);
		}

		/// <summary>An absolute http(s) URL with a host and no embedded credentials.</summary>
		public static bool TryNormalizeUrl(string value, out Uri url)
		{
			url = null;
			if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
				return false;

			if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed))
				return false;

			if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
				return false;

			if (string.IsNullOrWhiteSpace(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo))
				return false;

			url = parsed;
			return true;
		}
	}
}
