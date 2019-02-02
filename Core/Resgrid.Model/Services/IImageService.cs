namespace Resgrid.Model.Services
{
	public interface IImageService
	{
		byte[] GetImage(ImageTypes type, string id);
		void SaveImage(ImageTypes type, string id, byte[] image);
	}
}
