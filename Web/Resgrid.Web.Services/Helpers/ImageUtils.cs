

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Resgrid.Web.ServicesCore.Helpers
{
	public static class ImageUtils
	{
		public static Image Resize(Image image, int scaledWidth, int scaledHeight)
		{
			var image2 = image.Clone(x => x.Resize(scaledWidth, scaledHeight));

			return image2;
		}

		public static Image Crop(Image image, int x, int y, int width, int height)
		{
			//var croppedBitmap = new Bitmap(width, height);

			//using (var g = Graphics.FromImage(croppedBitmap))
			//{
			//	g.DrawImage(image,
			//			new Rectangle(0, 0, width, height),
			//			new Rectangle(x, y, width, height), GraphicsUnit.Pixel);
			//}

			var image2 = image.Clone(z => z.Crop(new Rectangle(x, y, width, height)));
			return image2;
			//return croppedBitmap;
		}
	}
}
