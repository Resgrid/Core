using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Lms;
using Resgrid.Services;
using Resgrid.Tests.TrainingT2;

namespace Resgrid.Tests.TrainingT5
{
	/// <summary>T5 item 3: SCORM 1.2 / 2004 and cmi5 package validation (manifest, zip-slip, size and file caps), upload and worker 98's unpacking.</summary>
	[TestFixture]
	public class LmsPackageTests
	{
		[Test]
		public void A_scorm_12_package_is_read_with_its_sco_mastery_score_and_launch_data()
		{
			using var zip = LmsStandardsWorld.Scorm12Zip();
			var inspection = LmsPackageInspector.Inspect(zip, zip.Length);

			inspection.Succeeded.Should().BeTrue(inspection.Error + " " + inspection.Detail);
			inspection.Manifest.Standard.Should().Be((int)LmsContentStandards.Scorm12);
			inspection.Manifest.Title.Should().Be("SCBA Basics");
			inspection.Manifest.Items.Should().ContainSingle("the asset resource is not a launchable SCO");
			var sco = inspection.Manifest.Items[0];
			sco.Href.Should().Be("index.html");
			sco.MasteryScore.Should().Be(80m);
			sco.LaunchData.Should().Be("start=1");
			inspection.FileCount.Should().Be(3);
		}

		[Test]
		public void A_scorm_2004_package_keeps_every_sco_its_parameters_and_the_scaled_passing_score()
		{
			using var zip = LmsStandardsWorld.Scorm2004Zip();
			var inspection = LmsPackageInspector.Inspect(zip, zip.Length);

			inspection.Succeeded.Should().BeTrue(inspection.Error + " " + inspection.Detail);
			inspection.Manifest.Standard.Should().Be((int)LmsContentStandards.Scorm2004);
			inspection.Manifest.Items.Select(i => i.Href).Should().Equal("part1/index.html", "part2/index.html?part=2");
			inspection.Manifest.Items[0].MasteryScore.Should().Be(0.7m);
			inspection.Manifest.Items[1].MasteryScore.Should().BeNull();
		}

		[Test]
		public void A_cmi5_package_lists_its_aus_with_move_on_and_mastery()
		{
			using var zip = LmsStandardsWorld.Cmi5Zip();
			var inspection = LmsPackageInspector.Inspect(zip, zip.Length);

			inspection.Succeeded.Should().BeTrue(inspection.Error + " " + inspection.Detail);
			inspection.Manifest.Standard.Should().Be((int)LmsContentStandards.Cmi5);
			inspection.Manifest.Identifier.Should().Be("https://example.test/course/ems-1");
			var au = inspection.Manifest.Items.Single();
			au.Identifier.Should().Be("https://example.test/au/1");
			au.MoveOn.Should().Be("CompletedAndPassed");
			au.MasteryScore.Should().Be(0.8m);
			au.Href.Should().Be("au1/index.html");
		}

		[TestCase("../evil.html")]
		[TestCase("folder/../../evil.html")]
		[TestCase("/etc/passwd")]
		[TestCase("dir\\evil.html")]
		[TestCase("C:evil.html")]
		[TestCase("a//b.html")]
		public void A_zip_slip_entry_refuses_the_whole_package(string entry)
		{
			using var zip = LmsStandardsWorld.Zip(("imsmanifest.xml", LmsStandardsWorld.Scorm12Manifest), ("index.html", "<html></html>"), (entry, "x"));
			var inspection = LmsPackageInspector.Inspect(zip, zip.Length);

			inspection.Error.Should().Be(LmsPackageErrors.UnsafePath);
			LmsPackageInspector.SafePath(entry).Should().BeNull();
		}

		[Test]
		public void Safe_paths_are_kept_as_relative_forward_slash_paths()
		{
			LmsPackageInspector.SafePath("res/img/logo.png").Should().Be("res/img/logo.png");
			LmsPackageInspector.SafePath("res/").Should().Be("res");
			LmsPackageInspector.LaunchPath("part%201/index.html?x=1#top").Should().Be("part 1/index.html");
			LmsPackageInspector.LaunchPath("https://elsewhere.test/index.html").Should().BeNull();
		}

		[Test]
		public void Missing_or_broken_manifests_and_non_zip_files_are_refused()
		{
			using (var zip = LmsStandardsWorld.Zip(("index.html", "<html></html>")))
				LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.ManifestMissing);
			using (var zip = LmsStandardsWorld.Zip(("imsmanifest.xml", "<manifest><broken"), ("index.html", "x")))
				LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.ManifestInvalid);
			using (var notZip = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip archive at all")))
				LmsPackageInspector.Inspect(notZip, notZip.Length).Error.Should().Be(LmsPackageErrors.NotZip);
			LmsPackageInspector.Inspect(new MemoryStream(), 0).Error.Should().Be(LmsPackageErrors.Empty);
		}

		[Test]
		public void A_manifest_with_a_dtd_is_refused_so_no_external_entity_is_ever_resolved()
		{
			var xxe = "<?xml version=\"1.0\"?><!DOCTYPE manifest [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]>" + LmsStandardsWorld.Scorm12Manifest.Substring(LmsStandardsWorld.Scorm12Manifest.IndexOf("<manifest", StringComparison.Ordinal)).Replace("SCBA Basics", "&x;");
			using var zip = LmsStandardsWorld.Zip(("imsmanifest.xml", xxe), ("index.html", "x"));
			LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.ManifestInvalid);
		}

		[Test]
		public void A_sco_or_au_naming_a_file_that_is_not_in_the_package_is_refused()
		{
			using (var zip = LmsStandardsWorld.Zip(("imsmanifest.xml", LmsStandardsWorld.Scorm12Manifest)))
				LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.LaunchFileMissing);
			using (var zip = LmsStandardsWorld.Zip(("cmi5.xml", LmsStandardsWorld.Cmi5Manifest.Replace("au1/index.html", "https://cdn.example.test/au1/index.html"))))
				LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.RemoteAuNotSupported);
			using (var zip = LmsStandardsWorld.Zip(("imsmanifest.xml", LmsStandardsWorld.Scorm12Manifest.Replace("adlcp:scormtype=\"sco\"", "adlcp:scormtype=\"asset\"")), ("index.html", "x")))
				LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.NoLaunchableItem);
			using (var zip = LmsStandardsWorld.Zip(("imsmanifest.xml", LmsStandardsWorld.Scorm12Manifest.Replace("<schemaversion>1.2</schemaversion>", "<schemaversion>CAM 9</schemaversion>")
				.Replace("http://www.adlnet.org/xsd/adlcp_rootv1p2", "urn:other")), ("index.html", "x")))
				LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.UnsupportedStandard);
		}

		[Test]
		public void Size_file_count_and_unpacked_size_caps_refuse_a_package()
		{
			var (maxBytes, maxFiles, maxUnpacked) = (LmsStandardsConfig.MaxPackageBytes, LmsStandardsConfig.MaxPackageFiles, LmsStandardsConfig.MaxUncompressedBytes);
			try
			{
				using var zip = LmsStandardsWorld.Scorm12Zip();
				LmsStandardsConfig.MaxPackageBytes = zip.Length - 1;
				LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.TooLarge);
				LmsStandardsConfig.MaxPackageBytes = maxBytes;

				zip.Position = 0;
				LmsStandardsConfig.MaxPackageFiles = 2;
				LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.TooManyFiles);
				LmsStandardsConfig.MaxPackageFiles = maxFiles;

				zip.Position = 0;
				LmsStandardsConfig.MaxUncompressedBytes = 20;
				LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.UncompressedTooLarge);
			}
			finally
			{
				LmsStandardsConfig.MaxPackageBytes = maxBytes;
				LmsStandardsConfig.MaxPackageFiles = maxFiles;
				LmsStandardsConfig.MaxUncompressedBytes = maxUnpacked;
			}
		}

		[Test]
		public void A_zip_bomb_entry_is_refused_by_its_compression_ratio()
		{
			using var zip = LmsStandardsWorld.Zip(("imsmanifest.xml", LmsStandardsWorld.Scorm12Manifest), ("index.html", "x"), ("bomb.bin", new string('a', 12 * 1024 * 1024)));
			LmsPackageInspector.Inspect(zip, zip.Length).Error.Should().Be(LmsPackageErrors.SuspiciousCompression);
		}

		[Test]
		public async Task An_upload_is_stored_queued_and_unpacked_by_worker_98_under_its_own_prefix()
		{
			var world = new LmsStandardsWorld();
			using var zip = LmsStandardsWorld.Scorm12Zip();
			var result = await world.PackageService.UploadAsync(world.Lms.AdminActor, "scba.zip", zip, zip.Length);

			result.Succeeded.Should().BeTrue();
			var package = (LmsContentPackage)result.Value;
			package.Status.Should().Be((int)LmsContentPackageStatuses.Queued);
			package.Sha256.Should().HaveLength(64);
			world.Objects.Should().ContainKey(package.SourceKey);

			(await world.PackageService.ProcessQueueAsync(5)).Should().Be(1);
			var stored = world.Packages.Rows.Single();
			stored.Status.Should().Be((int)LmsContentPackageStatuses.Ready);
			stored.FileCount.Should().Be(3);
			world.Objects.Keys.Where(k => k.StartsWith(stored.ContentPrefix, StringComparison.Ordinal)).Select(k => k.Substring(stored.ContentPrefix.Length))
				.Should().BeEquivalentTo("imsmanifest.xml", "index.html", "shared/style.css");
			stored.ContentPrefix.Should().StartWith("lms/" + LmsWorld.DepartmentId + "/0/packages/");
		}

		[Test]
		public async Task An_invalid_upload_stores_nothing_and_says_why()
		{
			var world = new LmsStandardsWorld();
			using var zip = LmsStandardsWorld.Zip(("imsmanifest.xml", LmsStandardsWorld.Scorm12Manifest), ("index.html", "x"), ("../escape.js", "x"));
			var result = await world.PackageService.UploadAsync(world.Lms.AdminActor, "bad.zip", zip, zip.Length);

			result.Error.Should().Be(LmsErrors.UnsupportedFile);
			((LmsPackageInspection)result.Value).Error.Should().Be(LmsPackageErrors.UnsafePath);
			world.Objects.Should().BeEmpty();
			world.Packages.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Uploading_fails_closed_without_the_add_on_the_store_or_the_right()
		{
			var world = new LmsStandardsWorld();
			using var zip = LmsStandardsWorld.Scorm12Zip();

			(await world.PackageService.UploadAsync(world.Lms.Actor(LmsWorld.Member), "a.zip", zip, zip.Length)).Error.Should().Be(LmsErrors.NotAuthorized);

			world.Lms.Gate = new LmsDepartmentGate { DepartmentId = LmsWorld.DepartmentId, ModuleOn = true, FlagOn = true, AddonActive = false, LastEntitledOn = DateTime.UtcNow.AddDays(-1) };
			(await world.PackageService.UploadAsync(world.Lms.AdminActor, "a.zip", zip, zip.Length)).Error.Should().Be(LmsErrors.AddonRequired, "lapse rule 3");

			world.Lms.Gate = new LmsDepartmentGate { DepartmentId = LmsWorld.DepartmentId, ModuleOn = true, FlagOn = true, AddonActive = null };
			(await world.PackageService.UploadAsync(world.Lms.AdminActor, "a.zip", zip, zip.Length)).Error.Should().Be(LmsErrors.AddonRequired, "billing could not answer: fail closed");

			world.Lms.Gate = new LmsDepartmentGate { DepartmentId = LmsWorld.DepartmentId, ModuleOn = true, FlagOn = true, AddonActive = true };
			world.Lms.Store.SetupGet(s => s.IsConfigured).Returns(false);
			(await world.PackageService.UploadAsync(world.Lms.AdminActor, "a.zip", zip, zip.Length)).Error.Should().Be(LmsErrors.MediaUnavailable);
			world.Packages.Rows.Should().BeEmpty();
		}

		[Test]
		public void Html_gets_the_adapter_right_after_its_head_and_nothing_else_changes()
		{
			var tags = Encoding.ASCII.GetBytes("<script src=\"a.js\"></script>");
			string Inject(string html) => Encoding.UTF8.GetString(Resgrid.Web.Controllers.LmsContentController.InjectIntoHead(Encoding.UTF8.GetBytes(html), tags));

			Inject("<html><HEAD lang=\"en\"><title>x</title></HEAD><body></body></html>").Should().Be("<html><HEAD lang=\"en\"><script src=\"a.js\"></script><title>x</title></HEAD><body></body></html>");
			Inject("<html><header></header><body></body></html>").Should().Be("<html><script src=\"a.js\"></script><header></header><body></body></html>", "a <header> element is not the head");
			Inject("<p>fragment</p>").Should().Be("<script src=\"a.js\"></script><p>fragment</p>");
			LmsPackageInspector.ContentTypeFor("a/b.HTML").Should().StartWith("text/html");
			LmsPackageInspector.ContentTypeFor("x.unknown").Should().Be("application/octet-stream");
		}

		[Test]
		public void The_content_policy_sandboxes_the_frame_and_keeps_its_connections_on_this_site()
		{
			var csp = Resgrid.Web.Controllers.LmsContentController.ContentSecurityPolicy;
			csp.Should().StartWith("sandbox allow-scripts");
			csp.Should().NotContain("allow-same-origin", "an opaque origin keeps Resgrid's cookies, storage and pages out of the package's reach");
			csp.Should().Contain("connect-src 'self'").And.Contain("frame-ancestors 'self'").And.Contain("object-src 'none'");
			var named = Resgrid.Web.Controllers.LmsContentController.ContentPolicyFor("https://resgrid.example.test");
			named.Should().Contain("connect-src 'self' https://resgrid.example.test;").And.NotContain("allow-same-origin");
			Resgrid.Web.Controllers.LmsContentController.ContentPolicyFor("javascript:alert(1)").Should().Be(csp);
		}

		[Test]
		public void The_version_hash_binds_a_standards_lessons_package_and_leaves_earlier_kinds_unchanged()
		{
			var version = new LmsCourseVersion { Title = "Course" };
			var modules = new[] { new LmsCourseModule { LmsCourseModuleId = 1, SortOrder = 1 } };
			var reading = new LmsLesson { LmsLessonId = 1, LmsCourseModuleId = 1, Kind = (int)LmsLessonKinds.Blocks, Title = "Read", ContentHtml = "<p>x</p>" };
			LmsCourseContent.Canonical(version, modules, new[] { reading }).Should().NotContain("standards", "approvals signed before T5 must still verify");

			LmsLesson Scorm(int packageId) => new LmsLesson { LmsLessonId = 2, LmsCourseModuleId = 1, Kind = (int)LmsLessonKinds.Scorm, Title = "SCO", LmsContentPackageId = packageId };
			LmsCourseContent.Canonical(version, modules, new[] { reading, Scorm(7) }).Should().Contain("\"standards\":{\"package\":7");
			LmsCourseContent.ComputeHash(version, modules, new[] { Scorm(7) }).Should().NotBe(LmsCourseContent.ComputeHash(version, modules, new[] { Scorm(8) }));
		}

		[Test]
		public async Task The_course_service_saves_a_standards_lesson_through_the_standards_check()
		{
			var world = new LmsStandardsWorld();
			var package = await world.ReadyPackageAsync(LmsStandardsWorld.Scorm12Zip());
			world.Lms.StandardsLessons.Setup(v => v.ApplyAsync(It.IsAny<LmsActor>(), It.IsAny<LmsLesson>(), It.IsAny<LmsLessonInput>()))
				.Returns((LmsActor a, LmsLesson l, LmsLessonInput i) => world.Validator.ApplyAsync(a, l, i));
			var created = await world.Lms.CourseService.CreateAsync(world.Lms.AdminActor, new LmsCourseInput { Title = "SCORM course" });
			created.Succeeded.Should().BeTrue(created.Error.ToString());
			var draft = created.Version;
			var module = world.Lms.Modules.Rows.First(m => m.LmsCourseVersionId == draft.LmsCourseVersionId);

			var refused = await world.Lms.CourseService.SaveLessonAsync(world.Lms.AdminActor, new LmsLessonInput
			{
				LmsCourseVersionId = draft.LmsCourseVersionId, LmsCourseModuleId = module.LmsCourseModuleId, Kind = (int)LmsLessonKinds.Cmi5, Title = "Wrong kind",
				LmsContentPackageId = package.LmsContentPackageId
			});
			refused.Error.Should().Be(LmsErrors.InvalidInput);

			var saved = await world.Lms.CourseService.SaveLessonAsync(world.Lms.AdminActor, new LmsLessonInput
			{
				LmsCourseVersionId = draft.LmsCourseVersionId, LmsCourseModuleId = module.LmsCourseModuleId, Kind = (int)LmsLessonKinds.Scorm, Title = "SCBA",
				LmsContentPackageId = package.LmsContentPackageId
			});
			saved.Succeeded.Should().BeTrue(saved.Error.ToString());
			((LmsLesson)saved.Value).LmsContentPackageId.Should().Be(package.LmsContentPackageId);
			(await world.Lms.CourseService.SaveLessonAsync(world.Lms.AdminActor, new LmsLessonInput
			{
				LmsCourseVersionId = draft.LmsCourseVersionId, LmsCourseModuleId = module.LmsCourseModuleId, Kind = (int)LmsLessonKinds.LiveSession, Title = "Live"
			})).Error.Should().Be(LmsErrors.InvalidInput, "live sessions stay reserved");
		}

		[Test]
		public async Task The_course_builder_accepts_only_the_departments_usable_package_of_the_right_standard()
		{
			var world = new LmsStandardsWorld();
			var scorm = await world.ReadyPackageAsync(LmsStandardsWorld.Scorm12Zip());
			var cmi5 = await world.ReadyPackageAsync(LmsStandardsWorld.Cmi5Zip());
			var actor = world.Lms.AdminActor;
			LmsLesson Lesson() => new LmsLesson();

			(await world.Validator.ApplyAsync(actor, Lesson(), new LmsLessonInput { Kind = (int)LmsLessonKinds.Scorm, LmsContentPackageId = scorm.LmsContentPackageId })).Should().Be(LmsErrors.None);
			(await world.Validator.ApplyAsync(actor, Lesson(), new LmsLessonInput { Kind = (int)LmsLessonKinds.Scorm, LmsContentPackageId = cmi5.LmsContentPackageId })).Should().Be(LmsErrors.InvalidInput);
			(await world.Validator.ApplyAsync(actor, Lesson(), new LmsLessonInput { Kind = (int)LmsLessonKinds.Cmi5, LmsContentPackageId = cmi5.LmsContentPackageId })).Should().Be(LmsErrors.None);
			(await world.Validator.ApplyAsync(actor, Lesson(), new LmsLessonInput { Kind = (int)LmsLessonKinds.Scorm, LmsContentPackageId = 999 })).Should().Be(LmsErrors.InvalidInput);

			var other = MemRepo<LmsContentPackage>.Copy(scorm);
			other.LmsContentPackageId = 0;
			other.DepartmentId = 99;
			await world.Packages.InsertAsync(other, default);
			(await world.Validator.ApplyAsync(actor, Lesson(), new LmsLessonInput { Kind = (int)LmsLessonKinds.Scorm, LmsContentPackageId = other.LmsContentPackageId }))
				.Should().Be(LmsErrors.InvalidInput, "another department's package");
		}
	}
}
