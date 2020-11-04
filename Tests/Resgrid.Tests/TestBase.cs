using System;
using System.IO;
using Autofac;
using NUnit.Framework;

namespace Resgrid.Tests
{
	public class TestBase
	{
		static TestBase()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;
			Resgrid.Config.SystemBehaviorConfig.UseInternalCache = true;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = true;

			//DbConfiguration.SetConfiguration(Resgrid.Repositories.DataRepository.Configurations.TestDbConfiguration);

			AppDomain.CurrentDomain.SetData("DataDirectory", AppDomain.CurrentDomain.BaseDirectory);

			if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + "\\Resgrid.mdf"))
				File.Delete(AppDomain.CurrentDomain.BaseDirectory + "\\Resgrid.mdf");

			if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + "\\Resgrid_log.ldf"))
				File.Delete(AppDomain.CurrentDomain.BaseDirectory + "\\Resgrid_log.ldf");

			//if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + "\\ResgridContext.sdf"))
			//	File.Delete(AppDomain.CurrentDomain.BaseDirectory + "\\ResgridContext.sdf");

			//File.Create(AppDomain.CurrentDomain.BaseDirectory + "\\ResgridContext.sdf");

			//Database.SetInitializer<DataContext>(new ResgridTestDbInitializer());

			//var migrator = new DbMigrator(new ResgridTestConfiguration());
			//migrator.Update();

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
