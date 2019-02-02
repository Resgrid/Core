using System.Data;

namespace Resgrid.Repositories.DataRepository.Transactions
{
    public class TestingIsolation : IISolationLevel
    {
        public IsolationLevel Level { get; private set; }

        public TestingIsolation()
        {
           Level = IsolationLevel.ReadCommitted;
        }
    }
}