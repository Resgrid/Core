namespace Resgrid.Model.Services
{
	public interface IFileService
	{
		File SaveFile(File file);
		File GetFileById(int fileId);
		void DeleteFile(File file);
	}
}
