using Newtonsoft.Json;

namespace Resgrid.Web.Services.Models
{
	public class CropRequest
	{
		/// <summary>
		/// your image path (the one we recieved after successfull upload)
		/// </summary>
		//[Alias("imgUrl")]
		//[JsonProperty(PropertyName = "imgUrl")]
		public string imgUrl { get; set; }

		/// <summary>
		/// your image original width (the one we recieved after upload)
		/// </summary>
		//[Alias("imgInitW")]
		//[JsonProperty(PropertyName = "imgInitW")]
		public int imgInitW { get; set; }

		/// <summary>
		/// your image original height (the one we recieved after upload)
		/// </summary>
		//[Alias("imgInitH")]
		//[JsonProperty(PropertyName = "imgInitH")]
		public int imgInitH { get; set; }

		/// <summary>
		/// your new scaled image width
		/// </summary>
		//[Alias("imgW")]ScaledWidth
		//[JsonProperty(PropertyName = "imgW")]
		public double imgW { get; set; }

		/// <summary>
		/// your new scaled image height
		/// </summary>
		//[Alias("imgH")]ScaledHeight
		//[JsonProperty(PropertyName = "imgH")]
		public double imgH { get; set; }

		/// <summary>
		/// top left corner of the cropped image in relation to scaled image
		/// </summary>
		//[Alias("imgX1")]CroppedX
		//[JsonProperty(PropertyName = "imgX1")]
		public int imgX1 { get; set; }

		/// <summary>
		/// top left corner of the cropped image in relation to scaled image
		/// </summary>
		//[Alias("imgY1")]CroppedY
		[JsonProperty(PropertyName = "imgY1")]
		public int imgY1 { get; set; }

		/// <summary>
		/// cropped image width
		/// </summary>
		//[Alias("cropW")]CroppedWidth
		[JsonProperty(PropertyName = "cropW")]
		public int cropW { get; set; }

		/// <summary>
		/// cropped image height
		/// </summary>
		//[Alias("cropH")]CroppedHeight
		[JsonProperty(PropertyName = "cropH")]
		public int cropH { get; set; }
	}
}
