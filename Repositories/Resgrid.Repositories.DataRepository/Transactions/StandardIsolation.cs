using System.Data;

namespace Resgrid.Repositories.DataRepository.Transactions
{
    public class StandardIsolation: IISolationLevel
    {
        public IsolationLevel Level { get; private set; }

        public StandardIsolation()
        {
           Level = IsolationLevel.ReadUncommitted;
        }
    }
}