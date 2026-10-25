using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// Serves department-uploaded unit, call and POI type icons, and the styled built-in map pins, to web pages from the site's
	/// own origin. Anonymous by design: the opaque IconKey is the capability, like the email masthead's MediaKey, and a new
	/// upload gets a new key, so the response is immutable and cached for a year.
	/// </summary>
	[AllowAnonymous]
	public class TypeIconsController : Controller
	{
		private readonly ITypeIconsService _typeIconsService;

		public TypeIconsController(ITypeIconsService typeIconsService)
		{
			_typeIconsService = typeIconsService;
		}

		/// <summary>The icon as a 128 px square PNG; with <paramref name="s"/> (a map icon style) it carries a badge of that colour.</summary>
		[HttpGet]
		[Route("TypeIcons/{key}")]
		public async Task<IActionResult> Get(string key, string s = null)
		{
			var icon = await _typeIconsService.GetIconByKeyAsync(key);
			if (icon?.Data == null || icon.Data.Length == 0)
				return NotFound();

			var data = icon.Data;
			var etag = "\"" + icon.Checksum + "\"";

			if (MapIconStyles.IsStyle(s))
			{
				data = MapIconPinRenderer.RenderBadge(icon.Data, s, icon.Checksum) ?? icon.Data;
				etag = $"\"{icon.Checksum}-{s}-{MapIconStyles.Version}\"";
			}

			return Image(data, etag);
		}

		/// <summary>A built-in map pin in a status or priority style as a 32 x 37 PNG, e.g. "ambulance-responding".</summary>
		[HttpGet]
		[Route("TypeIcons/Pins/{name}")]
		public IActionResult GetPin(string name)
		{
			var data = MapIconPinRenderer.RenderPin(name);
			if (data == null)
				return NotFound();

			return Image(data, $"\"{name}-{MapIconStyles.Version}\"");
		}

		private IActionResult Image(byte[] data, string etag)
		{
			if (Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) && ifNoneMatch.ToString() == etag)
				return StatusCode(304);

			Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
			Response.Headers["ETag"] = etag;
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			return File(data, "image/png");
		}
	}
}
