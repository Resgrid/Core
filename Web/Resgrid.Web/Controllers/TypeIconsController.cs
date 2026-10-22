using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;

namespace Resgrid.Web.Controllers
{
	/// <summary>
	/// Serves department-uploaded unit, call and POI type icons to web pages from the site's own origin. Anonymous by design: the
	/// opaque IconKey is the capability, like the email masthead's MediaKey, and a new upload gets a new key, so the
	/// response is immutable and cached for a year.
	/// </summary>
	[AllowAnonymous]
	public class TypeIconsController : Controller
	{
		private readonly ITypeIconsService _typeIconsService;

		public TypeIconsController(ITypeIconsService typeIconsService)
		{
			_typeIconsService = typeIconsService;
		}

		[HttpGet]
		[Route("TypeIcons/{key}")]
		public async Task<IActionResult> Get(string key)
		{
			var icon = await _typeIconsService.GetIconByKeyAsync(key);
			if (icon?.Data == null || icon.Data.Length == 0)
				return NotFound();

			var etag = "\"" + icon.Checksum + "\"";
			if (Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) && ifNoneMatch.ToString() == etag)
				return StatusCode(304);

			Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
			Response.Headers["ETag"] = etag;
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			return File(icon.Data, "image/png");
		}
	}
}
