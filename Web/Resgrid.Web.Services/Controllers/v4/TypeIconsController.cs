using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Department-uploaded unit, call and POI type icons, linked from the TypeIconUrl / IconUrl fields of the unit, call, POI, dispatch and map
	/// results, and the styled built-in map pins linked from map markers' PinIconUrl. Anonymous so image components can load
	/// them without an auth header: the opaque key is the capability, and a new upload gets a new key, so responses are immutable.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[AllowAnonymous]
	public class TypeIconsController : ControllerBase
	{
		private readonly ITypeIconsService _typeIconsService;

		public TypeIconsController(ITypeIconsService typeIconsService)
		{
			_typeIconsService = typeIconsService;
		}

		/// <summary>The icon as a 128 px square PNG.</summary>
		/// <param name="key">The icon key at the end of a TypeIconUrl.</param>
		/// <param name="s">Optional status or priority style (an IconStyle value): the icon is returned with a badge of that colour.</param>
		[HttpGet("{key}")]
		[Produces(MediaTypeNames.Image.Png)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status304NotModified)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> Get(string key, string s = null)
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

		/// <summary>A built-in map pin in a status or priority style as a 32 x 37 PNG, e.g. "ambulance-responding" or "call-high".</summary>
		/// <param name="name">The pin name at the end of a PinIconUrl.</param>
		[HttpGet("Pins/{name}")]
		[Produces(MediaTypeNames.Image.Png)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status304NotModified)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public ActionResult GetPin(string name)
		{
			var data = MapIconPinRenderer.RenderPin(name);
			if (data == null)
				return NotFound();

			return Image(data, $"\"{name}-{MapIconStyles.Version}\"");
		}

		private ActionResult Image(byte[] data, string etag)
		{
			if (Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch) && ifNoneMatch.ToString() == etag)
				return StatusCode(StatusCodes.Status304NotModified);

			Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
			Response.Headers["ETag"] = etag;
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			return File(data, "image/png");
		}
	}
}
