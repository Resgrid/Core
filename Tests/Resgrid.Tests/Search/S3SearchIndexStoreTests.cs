using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Search;
using Resgrid.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// A credential rejection from the object store (RESGRID-API-H: SignatureDoesNotMatch on every pull in one region) is
	/// rethrown with the settings the pod signed with, so the event names what to fix, without ever carrying the secret.
	/// Not-found and conditional-PUT outcomes keep their existing meaning.
	/// </summary>
	[TestFixture]
	public class S3SearchIndexStoreTests
	{
		private const string Secret = "s3cr3t-value-that-must-not-leak-000000000";

		private string _endpoint, _accessKey, _secretKey, _bucket, _region, _prefix;

		[SetUp]
		public void SetUp()
		{
			_endpoint = SearchConfig.S3Endpoint;
			_accessKey = SearchConfig.S3AccessKey;
			_secretKey = SearchConfig.S3SecretKey;
			_bucket = SearchConfig.S3Bucket;
			_region = SearchConfig.S3Region;
			_prefix = SearchConfig.S3Prefix;

			SearchConfig.S3Endpoint = "https://rustfs.eu.example:9000";
			SearchConfig.S3AccessKey = "AKEU1234567890";
			SearchConfig.S3SecretKey = Secret + "\n";
			SearchConfig.S3Bucket = "resgrid-search";
			SearchConfig.S3Region = "eu-central-1";
			SearchConfig.S3Prefix = "search";
		}

		[TearDown]
		public void TearDown()
		{
			SearchConfig.S3Endpoint = _endpoint;
			SearchConfig.S3AccessKey = _accessKey;
			SearchConfig.S3SecretKey = _secretKey;
			SearchConfig.S3Bucket = _bucket;
			SearchConfig.S3Region = _region;
			SearchConfig.S3Prefix = _prefix;
		}

		private static AmazonS3Exception S3Error(HttpStatusCode status, string code)
		{
			return new AmazonS3Exception("store error") { StatusCode = status, ErrorCode = code };
		}

		[Test]
		public async Task Signature_mismatch_names_the_settings_in_effect_but_never_the_secret()
		{
			var client = new Mock<IAmazonS3>();
			client.Setup(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(S3Error(HttpStatusCode.Forbidden, "SignatureDoesNotMatch"));
			var store = new S3SearchIndexStore(client.Object);

			Func<Task> act = () => store.GetManifestAsync(SearchIndexNames.Global);

			var thrown = (await act.Should().ThrowAsync<SearchIndexStoreAccessException>()).Which;
			thrown.InnerException.Should().BeOfType<AmazonS3Exception>();
			thrown.Message.Should().Contain("403 SignatureDoesNotMatch")
				.And.Contain("S3SecretKey")
				.And.Contain("endpoint 'https://rustfs.eu.example:9000'")
				.And.Contain("bucket 'resgrid-search'")
				.And.Contain("region 'eu-central-1'")
				.And.Contain("access key 'AKEU...'")
				.And.Contain($"secret key {Secret.Length} chars");
			thrown.Message.Should().NotContain(Secret).And.NotContain("AKEU1234567890");
		}

		[Test]
		public async Task Access_failures_on_publish_paths_are_translated_too()
		{
			var client = new Mock<IAmazonS3>();
			client.Setup(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(S3Error(HttpStatusCode.Forbidden, "InvalidAccessKeyId"));
			client.Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(S3Error(HttpStatusCode.Forbidden, "AccessDenied"));
			var store = new S3SearchIndexStore(client.Object);

			Func<Task> list = () => store.ListFilesAsync(SearchIndexNames.Global);
			Func<Task> upload = () => store.UploadFileAsync(SearchIndexNames.Global, "_0.cfs", "/tmp/_0.cfs");
			Func<Task> manifest = () => store.PutManifestAsync(SearchIndexNames.Global, new SearchIndexManifest { IndexName = SearchIndexNames.Global }, null);

			(await list.Should().ThrowAsync<SearchIndexStoreAccessException>()).Which.Message.Should().Contain("InvalidAccessKeyId");
			(await upload.Should().ThrowAsync<SearchIndexStoreAccessException>()).Which.Message.Should().Contain("AccessDenied");
			await manifest.Should().ThrowAsync<SearchIndexStoreAccessException>();
		}

		[Test]
		public async Task Denied_per_object_deletes_are_access_failures_and_keep_every_code()
		{
			// The store answers a multi-object delete 200 with per-key errors; the SDK raises DeleteObjectsException with no
			// status or error code of its own.
			var response = new DeleteObjectsResponse
			{
				DeleteErrors = new List<DeleteError>
				{
					new DeleteError { Key = "search/global/_0.cfs", Code = "AccessDenied" },
					new DeleteError { Key = "search/global/_1.cfs", Code = "AccessDenied" },
					new DeleteError { Key = "search/global/_2.cfs", Code = "InternalError" }
				}
			};
			var client = new Mock<IAmazonS3>();
			client.Setup(c => c.DeleteObjectsAsync(It.IsAny<DeleteObjectsRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new DeleteObjectsException(response));
			var store = new S3SearchIndexStore(client.Object);

			Func<Task> act = () => store.DeleteFilesAsync(SearchIndexNames.Global, new[] { "_0.cfs", "_1.cfs", "_2.cfs" });

			var thrown = (await act.Should().ThrowAsync<SearchIndexStoreAccessException>()).Which;
			thrown.InnerException.Should().BeOfType<DeleteObjectsException>().Which.Response.Should().BeSameAs(response);
			thrown.Message.Should().Contain("delete of 3 object(s): 2 AccessDenied, 1 InternalError")
				.And.Contain("no grant for this operation")
				.And.Contain("bucket 'resgrid-search'");
			thrown.Message.Should().NotContain(Secret);
		}

		[Test]
		public async Task Per_object_delete_errors_without_an_access_code_keep_their_own_exception()
		{
			var response = new DeleteObjectsResponse { DeleteErrors = new List<DeleteError> { new DeleteError { Key = "search/global/_0.cfs", Code = "InternalError" } } };
			var client = new Mock<IAmazonS3>();
			client.Setup(c => c.DeleteObjectsAsync(It.IsAny<DeleteObjectsRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new DeleteObjectsException(response));
			var store = new S3SearchIndexStore(client.Object);

			Func<Task> act = () => store.DeleteFilesAsync(SearchIndexNames.Global, new[] { "_0.cfs" });

			await act.Should().ThrowAsync<DeleteObjectsException>();
		}

		[Test]
		public async Task Unrecognized_forbidden_codes_are_not_reported_as_a_missing_grant()
		{
			var client = new Mock<IAmazonS3>();
			client.Setup(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(S3Error(HttpStatusCode.Forbidden, "InvalidSecurity"));
			var store = new S3SearchIndexStore(client.Object);

			Func<Task> act = () => store.GetManifestAsync(SearchIndexNames.Global);

			var thrown = (await act.Should().ThrowAsync<SearchIndexStoreAccessException>()).Which;
			thrown.Message.Should().Contain("403 InvalidSecurity")
				.And.Contain("not one of the credential faults recognized here")
				.And.NotContain("credentials were accepted");
		}

		[Test]
		public async Task Missing_manifest_still_reads_as_no_published_revision()
		{
			var client = new Mock<IAmazonS3>();
			client.Setup(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(S3Error(HttpStatusCode.NotFound, "NoSuchKey"));
			var store = new S3SearchIndexStore(client.Object);

			(await store.GetManifestAsync(SearchIndexNames.Global)).Should().BeNull();
		}

		[Test]
		public async Task Lost_conditional_manifest_put_is_still_a_conflict()
		{
			var client = new Mock<IAmazonS3>();
			client.Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(S3Error(HttpStatusCode.PreconditionFailed, "PreconditionFailed"));
			var store = new S3SearchIndexStore(client.Object);

			Func<Task> act = () => store.PutManifestAsync(SearchIndexNames.Global, new SearchIndexManifest { IndexName = SearchIndexNames.Global }, "\"etag\"");

			await act.Should().ThrowAsync<SearchIndexManifestConflictException>();
		}
	}
}
