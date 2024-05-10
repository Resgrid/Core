using Autofac;
using NUnit.Framework;
using Resgrid.Tests;

namespace Resgrid.SmokeTests
{
	public class SmokeTestBase
	{
		static SmokeTestBase()
		{
			Bootstrapper.Initialize();
		}

		protected T Resolve<T>()
		{
			return Bootstrapper.GetKernel().Resolve<T>();
		}

		[SetUp]
		public void SetupContext_ALL()
		{
			Before_all_tests();
		}

		[TearDown]
		public void TearDownContext_ALL()
		{
			After_all_tests();
		}

		protected virtual void Before_all_tests()
		{
		}

		protected virtual void After_all_tests()
		{
		}
	}
}
