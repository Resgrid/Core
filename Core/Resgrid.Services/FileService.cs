using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using System.Linq;

namespace Resgrid.Services
{
	public class FileService : IFileService
	{
		private readonly IGenericDataRepository<File> _fileRepository;

		public FileService(IGenericDataRepository<File> fileRepository)
		{
			_fileRepository = fileRepository;
		}

		public File SaveFile(File file)
		{
			_fileRepository.SaveOrUpdate(file);

			return file;
		}

		public File GetFileById(int fileId)
		{
			return _fileRepository.GetAll().Where(x => x.FileId == fileId).FirstOrDefault();
		}

		public void DeleteFile(File file)
		{
			_fileRepository.DeleteOnSubmit(file);
		}
	}
}