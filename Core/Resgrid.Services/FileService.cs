using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class FileService : IFileService
	{
		private readonly IFileRepository _fileRepository;

		public FileService(IFileRepository fileRepository)
		{
			_fileRepository = fileRepository;
		}

		public async Task<File> SaveFileAsync(File file, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _fileRepository.SaveOrUpdateAsync(file, cancellationToken);
		}

		public async Task<File> GetFileByIdAsync(int fileId)
		{
			return await _fileRepository.GetByIdAsync(fileId);
		}

		public async Task<bool> DeleteFileAsync(File file, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _fileRepository.DeleteAsync(file, cancellationToken);
		}
	}
}
