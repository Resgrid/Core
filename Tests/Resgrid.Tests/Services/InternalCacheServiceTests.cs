//using System;
//using FluentAssertions;
//using NUnit.Framework;
//using Resgrid.Model.Services;

//namespace Resgrid.Tests.Services
//{
//	namespace InternalCacheServiceTests
//	{
//		public class with_the_internalCache_service : TestBase
//		{
//			protected IInternalCacheService _internalCacheService;

//			protected with_the_internalCache_service()
//			{
//				_internalCacheService = Resolve<IInternalCacheService>();
//			}
//		}

//		[TestFixture]
//		public class when_adding_items_to_the_cache : with_the_internalCache_service
//		{
//			[Test]
//			public void should_add_without_issue()
//			{
//				string key = "TEST1";
//				var test = new
//				{
//					Id = 1,
//					Value = "TEST1"
//				};

//				_internalCacheService.AddOrUpdateCache(key, test, TimeSpan.FromMinutes(60));
//			}

//			[Test]
//			public void should_update_without_issue()
//			{
//				string key = "TEST2";
//				var test = new
//				{
//					Id = 1,
//					Value = "TEST2"
//				};

//				var test2 = new
//				{
//					Id = 1,
//					Value = "TEST2"
//				};

//				_internalCacheService.AddOrUpdateCache(key, test, TimeSpan.FromMinutes(60));
//				_internalCacheService.AddOrUpdateCache(key, test2, TimeSpan.FromMinutes(60));
//			}
//		}

//		[TestFixture]
//		public class when_retrieving_items_to_the_cache : with_the_internalCache_service
//		{
//			[Test]
//			public void should_get_same_item()
//			{
//				string key = "TEST3";
//				var test = new
//				{
//					Id = 3,
//					Value = "TEST3"
//				};

//				_internalCacheService.AddOrUpdateCache(key, test, TimeSpan.FromMinutes(60));
//				var cachedObj = _internalCacheService.GetCachedData<dynamic>(key, TimeSpan.FromMinutes(60));

//				test.Id.Should().Be(test.Id);
//				test.Value.Should().Be(cachedObj.Value);
//			}

//			[Test]
//			public void should_update_and_get_new_item()
//			{
//				string key = "TEST4";
//				var test = new
//				{
//					Id = 4,
//					Value = "xxxxxx"
//				};

//				var test2 = new
//				{
//					Id = 41,
//					Value = "TEST4"
//				};

//				_internalCacheService.AddOrUpdateCache(key, test, TimeSpan.FromMinutes(60));
//				_internalCacheService.AddOrUpdateCache(key, test2, TimeSpan.FromMinutes(60));

//				var cachedObj = _internalCacheService.GetCachedData<dynamic>(key, TimeSpan.FromMinutes(60));

//				test2.Id.Should().Be(cachedObj.Id);
//				test2.Value.Should().Be(cachedObj.Value);
//			}
//		}

//		[TestFixture]
//		public class when_removing_items_from_the_cache : with_the_internalCache_service
//		{
//			[Test]
//			public void should_remove_empty_wihtout_issue()
//			{
//				string key = "TEST5";
//				_internalCacheService.Invalidate(key);
//			}

//			[Test]
//			public void should_get_null_for_cleared_item()
//			{
//				string key = "TEST6";
//				var test = new
//				{
//					Id = 6,
//					Value = "TEST6"
//				};

//				_internalCacheService.AddOrUpdateCache(key, test, TimeSpan.FromMinutes(60));
//				_internalCacheService.Invalidate(key);

//				var cacheObj = _internalCacheService.GetCachedData<dynamic>(key, TimeSpan.FromMinutes(60));

//				bool isNull = false || cacheObj == null;

//				isNull.Should().Be(true);
//			}
//		}
//	}
//}