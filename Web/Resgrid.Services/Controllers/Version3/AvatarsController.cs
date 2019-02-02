using System;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Used to interact with the user avatars (profile pictures) in the Resgrid system. The authentication header isn't required to access this method.
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
	public class AvatarsController : ApiController
	{
		private readonly IImageService _imageService;

		public AvatarsController(IImageService imageService)
		{
			_imageService = imageService;
		}

		/// <summary>
		/// Get a users avatar from the Resgrid system based on their ID
		/// </summary>
		/// <param name="id">ID of the user</param>
		/// <returns></returns>
		[HttpGet]
		public HttpResponseMessage Get(string id, int? type)
		{
			var result = new HttpResponseMessage(HttpStatusCode.OK);


			if (type == null)
			{ // User profile images (will have a null type) are by Guid
				Guid newId;
				if (!Guid.TryParse(id, out newId))
					return new HttpResponseMessage(HttpStatusCode.NotFound);
			}
			else if (type != null && type == 1)
			{ // User profile images (will have a 1 type) are by Guid
				Guid newId;
				if (!Guid.TryParse(id, out newId))
					return new HttpResponseMessage(HttpStatusCode.NotFound);
			}
			else if (type != null && type == 2)
			{ // Department Profiles are by Int
				int newId;
				if (!int.TryParse(id, out newId))
					return new HttpResponseMessage(HttpStatusCode.NotFound);
			}


			byte[] data = null;
			if (type == null)
				data = _imageService.GetImage(ImageTypes.Avatar, id);
			else
				data = _imageService.GetImage((ImageTypes)type.Value, id);

			if (data == null || data.Length <= 0)
				return Request.CreateResponse(HttpStatusCode.NotFound);

			result.Content = new ByteArrayContent(data);
			result.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

			return result;
		}

		/// <summary>
		/// Get a users avatar from the Resgrid system based on their ID
		/// </summary>
		/// <param name="id">ID of the user</param>
		/// <returns></returns>
		[HttpGet]
		public HttpResponseMessage Get(string id)
		{
			var result = new HttpResponseMessage(HttpStatusCode.OK);

			// User profile images (will have a null type) are by Guid
			Guid newId;
			if (!Guid.TryParse(id, out newId))
				return new HttpResponseMessage(HttpStatusCode.NotFound);

			byte[] data = _imageService.GetImage(ImageTypes.Avatar, id);

			if (data == null || data.Length <= 0)
				return Request.CreateResponse(HttpStatusCode.NotFound);

			result.Content = new ByteArrayContent(data);
			result.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

			return result;
		}

		[HttpPost]
		public HttpResponseMessage Upload([FromUri] string id, int? type)
		{
			var img = HttpContext.Current.Request.Files.Count > 0 ?
				HttpContext.Current.Request.Files[0] : null;

			// check for a valid mediatype
			if (!img.ContentType.StartsWith("image/"))
				throw HttpStatusCode.BadRequest.AsException();

			// load the image from the upload and generate a new filename
			var image = Image.FromStream(img.InputStream);
			var extension = Path.GetExtension(img.FileName);

			ImageConverter converter = new ImageConverter();
			byte[] imgArray = (byte[])converter.ConvertTo(image, typeof(byte[]));

			if (type == null)
				_imageService.SaveImage(ImageTypes.Avatar, id, imgArray);
			else
				_imageService.SaveImage((ImageTypes)type.Value, id, imgArray);

			var baseUrl = ConfigurationManager.AppSettings["ResgridApiUrl"];

			string url;

			if (type == null)
				url = baseUrl + "/api/v3/Avatars/Get?id=" + id;
			else
				url = baseUrl + "/api/v3/Avatars/Get?id=" + id + "?type=" + type.Value;

			var obj = new
			{
				status = CroppicStatuses.Success,
				url = url,
				width = image.Width,
				height = image.Height
			};

			return Request.CreateResponse(HttpStatusCode.OK, obj);
			//return obj;
			//return SerializedObject(obj);
		}

		[HttpPut]
		public HttpResponseMessage Crop([FromBody]CropRequest model)
		{
			// extract original image ID and generate a new filename for the cropped result
			var originalUri = new Uri(model.imgUrl);
			var originalId = originalUri.Query.Replace("?id=", "");//.Last();
																   //var extension = Path.GetExtension(originalId);
																   //var croppedId = GenerateIdFor(model.CroppedWidth, model.CroppedHeight, extension);

			try
			{
				var ms = new MemoryStream(_imageService.GetImage(ImageTypes.Avatar, originalId));
				var img = Image.FromStream(ms);

				// load the original picture and resample it to the scaled values
				var bitmap = ImageUtils.Resize(img, (int)model.imgW, (int)model.imgH);

				var croppedBitmap = ImageUtils.Crop(bitmap, model.imgX1, model.imgY1, model.cropW, model.cropH);

				ImageConverter converter = new ImageConverter();
				byte[] imgArray = (byte[])converter.ConvertTo(croppedBitmap, typeof(byte[]));

				_imageService.SaveImage(ImageTypes.Avatar, originalId, imgArray);
			}
			catch (Exception e)
			{
				throw HttpStatusCode.InternalServerError.AsException();
			}

			var obj = new
			{
				status = CroppicStatuses.Success,
				url = originalId
			};

			return Request.CreateResponse(HttpStatusCode.OK, obj);
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}

	internal static class CroppicStatuses
	{
		public const string Success = "success";
		public const string Error = "error";
	}
}
