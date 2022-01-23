using System;
using System.IO;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;


using Resgrid.Web.Services.Models;
using Resgrid.Web.ServicesCore.Helpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Used to interact with the user avatars (profile pictures) in the Resgrid system. The authentication header isn't required to access this method.
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	//[EnableCors("_resgridWebsiteAllowSpecificOrigins")]
	public class AvatarsController : ControllerBase
	{
		private readonly IImageService _imageService;
		private static byte[] _defaultProfileImage;

		public AvatarsController(IImageService imageService)
		{
			_imageService = imageService;
		}

		/// <summary>
		/// Get a users avatar from the Resgrid system based on their ID
		/// </summary>
		/// <param name="id">ID of the user</param>
		/// <returns></returns>
		[HttpGet("Get")]
		[Produces(MediaTypeNames.Image.Jpeg)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> Get(string id, int? type)
		{
			byte[] data = null;
			if (type == null)
				data = await _imageService.GetImageAsync(ImageTypes.Avatar, id);
			else
				data = await _imageService.GetImageAsync((ImageTypes)type.Value, id);

			if (data == null || data.Length <= 0)
				return File(GetDefaultProfileImage(), "image/png");

			return File(data, "image/jpeg");
		}

		///// <summary>
		///// Get a users avatar from the Resgrid system based on their ID
		///// </summary>
		///// <param name="id">ID of the user</param>
		///// <returns></returns>
		//[HttpGet("Get")]
		//[Produces(MediaTypeNames.Image.Jpeg)]
		//[ProducesResponseType(StatusCodes.Status200OK)]
		//[ProducesResponseType(StatusCodes.Status404NotFound)]
		//public async Task<ActionResult> Get(string id)
		//{
		//	var result = Ok();

		//	// User profile images (will have a null type) are by Guid
		//	Guid newId;
		//	if (!Guid.TryParse(id, out newId))
		//		return NotFound();

		//	byte[] data = await _imageService.GetImageAsync(ImageTypes.Avatar, id);

		//	if (data == null || data.Length <= 0)
		//		return NotFound();

		//	return File(data, "image/jpeg");
		//}

		[HttpPost("Upload")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult> Upload([FromQuery] string id, int? type)
		{
			var img = HttpContext.Request.Form.Files.Count > 0 ?
				HttpContext.Request.Form.Files[0] : null;

			// check for a valid mediatype
			if (!img.ContentType.StartsWith("image/"))
				return BadRequest();

			// load the image from the upload and generate a new filename
			//var image = Image.FromStream(img.OpenReadStream());
			var extension = Path.GetExtension(img.FileName);
			byte[] imgArray;
			int width = 0;
			int height = 0;

			using (Image image = Image.Load(img.OpenReadStream()))
			{
				//image.Mutate(x => x
				//	 .Resize(image.Width / 2, image.Height / 2)
				//	 .Grayscale());

				width = image.Width;
				height = image.Height;

				MemoryStream ms = new MemoryStream();
				await image.SaveAsPngAsync(ms);
				imgArray = ms.ToArray();

				//image.Save()"output/fb.png"); // Automatic encoder selected based on extension.
			}

			//ImageConverter converter = new ImageConverter();
			//byte[] imgArray = (byte[])converter.ConvertTo(image, typeof(byte[]));

			if (type == null)
				await _imageService.SaveImageAsync(ImageTypes.Avatar, id, imgArray);
			else
				await _imageService.SaveImageAsync((ImageTypes)type.Value, id, imgArray);

			var baseUrl = Config.SystemBehaviorConfig.ResgridApiBaseUrl;

			string url;

			if (type == null)
				url = baseUrl + "/api/v3/Avatars/Get?id=" + id;
			else
				url = baseUrl + "/api/v3/Avatars/Get?id=" + id + "?type=" + type.Value;

			var obj = new
			{
				status = CroppicStatuses.Success,
				url = url,
				width = width,
				height = height
			};

			return CreatedAtAction(nameof(Upload), new { id = obj.url }, obj);
		}

		[HttpPut("Crop")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult> Crop([FromBody]CropRequest model)
		{
			// extract original image ID and generate a new filename for the cropped result
			var originalUri = new Uri(model.imgUrl);
			var originalId = originalUri.Query.Replace("?id=", "");//.Last();
																   //var extension = Path.GetExtension(originalId);
																   //var croppedId = GenerateIdFor(model.CroppedWidth, model.CroppedHeight, extension);

			try
			{
				var ms = new MemoryStream(await _imageService.GetImageAsync(ImageTypes.Avatar, originalId));
				//var img = Image.FromStream(ms);

				byte[] imgArray;

				Image image = Image.Load(ms);

				// load the original picture and resample it to the scaled values
				var bitmap = ImageUtils.Resize(image, (int)model.imgW, (int)model.imgH);

				var croppedBitmap = ImageUtils.Crop(bitmap, model.imgX1, model.imgY1, model.cropW, model.cropH);

				MemoryStream ms2 = new MemoryStream();
				await croppedBitmap.SaveAsPngAsync(ms2);
				imgArray = ms.ToArray();

				//ImageConverter converter = new ImageConverter();
				//byte[] imgArray = (byte[])converter.ConvertTo(croppedBitmap, typeof(byte[]));

				await _imageService.SaveImageAsync(ImageTypes.Avatar, originalId, imgArray);
			}
			catch (Exception e)
			{
				return BadRequest();
			}

			var obj = new
			{
				status = CroppicStatuses.Success,
				url = originalId
			};

			return CreatedAtAction(nameof(Crop), new { id = obj.url }, obj);
		}

		//public HttpResponseMessage Options()
		//{
		//	var response = new HttpResponseMessage();
		//	response.StatusCode = HttpStatusCode.OK;
		//	response.Headers.Add("Access-Control-Allow-Origin", "*");
		//	response.Headers.Add("Access-Control-Request-Headers", "*");
		//	response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

		//	return response;
		//}

		private byte[] GetDefaultProfileImage()
		{
			if (_defaultProfileImage == null)
				_defaultProfileImage = EmbeddedResources.GetApiRequestFile(typeof(AvatarsController), "Resgrid.Web.Services.Properties.Resources.defaultProfile.png");
			
			return _defaultProfileImage;
		}
	}

	internal static class CroppicStatuses
	{
		public const string Success = "success";
		public const string Error = "error";
	}
}
