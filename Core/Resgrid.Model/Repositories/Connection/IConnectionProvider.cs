using System.Data.Common;

namespace Resgrid.Model.Repositories.Connection
{
	public interface IConnectionProvider
	{
		DbConnection Create();
	}
}
