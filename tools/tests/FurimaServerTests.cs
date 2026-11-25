using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;

public static class FurimaServerTests
{
    private static int passed = 0;
    private static int failed = 0;

    private static void AssertTrue(bool cond, string message)
    {
        if (!cond) throw new Exception("Assertion failed: " + message);
    }

    private static void AssertEquals(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new Exception(message + " (expected='" + expected + "', actual='" + actual + "')");
        }
    }

    private static void RunTest(string name, Action body)
    {
        try
        {
            body();
            passed++;
            Console.WriteLine("[PASS] " + name);
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine("[FAIL] " + name + ": " + ex.Message);
        }
    }

    public static int Main(string[] args)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "furima_cs_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            RunTest("DashboardHtml embeds FurimaDashboard.html and escapes authToken", () =>
            {
                string html = DashboardHtml.GetHtml("tok'123");
                AssertTrue(html.Contains("<!DOCTYPE html>"), "HTML header missing");
                AssertTrue(html.Contains("const AUTH_TOKEN = 'tok\\'123';"), "Escaped AUTH_TOKEN missing");
                AssertTrue(!html.Contains("__AUTH_TOKEN__"), "Placeholder was not replaced");
            });

            RunTest("AtomicFileWriter writes .bak and recovers when primary file is missing", () =>
            {
                string path = Path.Combine(tempRoot, "sample.json");
                AtomicFileWriter.WriteTextAtomic(path, "{\"v\":1}", true);
                AtomicFileWriter.WriteTextAtomic(path, "{\"v\":2}", true);
                AssertTrue(File.Exists(path + ".bak"), ".bak file missing");
                File.Delete(path);

                bool recovered;
                string err;
                var loaded = AtomicFileWriter.ReadJsonWithBackup<Dictionary<string, int>>(path, d => d != null && d.ContainsKey("v"), out recovered, out err);
                AssertTrue(recovered, "Should recover from .bak when primary is missing");
                AssertTrue(loaded != null && loaded["v"] == 1, "Recovered value mismatch");
            });

            RunTest("ReferencePhotoStore recovers from .bak alone and restores primary json", () =>
            {
                string syncDir = Path.Combine(tempRoot, "sync_ref_test");
                Directory.CreateDirectory(syncDir);
                SyncStore.SetSyncDirForTest(syncDir);
                ReferencePhotoStore.ResetCacheForTest();

                string refPath = Path.Combine(syncDir, "claude_references.json");
                string md5 = "0123456789abcdef0123456789abcdef";
                string json = "{\"references\":[{\"md5\":\"" + md5 + "\",\"displayName\":\"test\",\"fileName\":\"ref.jpg\",\"addedAt\":12345}]}";
                File.WriteAllText(refPath + ".bak", json, Encoding.UTF8);

                var refs = ReferencePhotoStore.GetEntries();
                AssertTrue(refs.Count == 1, "Expected 1 recovered reference, got " + refs.Count);
                AssertEquals(md5, refs[0].md5, "Recovered MD5 mismatch");
                AssertTrue(File.Exists(refPath), "Primary claude_references.json should be restored on disk");
            });

            RunTest("ClaudeJobStore.IsValidMd5 validates 32-char lowercase hex", () =>
            {
                AssertTrue(ReferencePhotoStore.IsValidMd5("0123456789abcdef0123456789abcdef"), "Valid MD5 rejected");
                AssertTrue(!ReferencePhotoStore.IsValidMd5("0123456789ABCDEF0123456789ABCDEF"), "Uppercase should be rejected");
                AssertTrue(!ReferencePhotoStore.IsValidMd5("short"), "Short string should be rejected");
            });

            RunTest("FurimaServerNative.MatchesIfNoneMatch handles quoted, weak, multi, and wildcard ETags", () =>
            {
                string h = "abcdef0123456789abcdef0123456789";
                string etag = "\"" + h + "\"";
                AssertTrue(FurimaServerNative.MatchesIfNoneMatch(etag, etag, h), "Exact ETag failed");
                AssertTrue(FurimaServerNative.MatchesIfNoneMatch("W/" + etag, etag, h), "Weak ETag failed");
                AssertTrue(FurimaServerNative.MatchesIfNoneMatch("\"other\", " + etag, etag, h), "Multi-ETag failed");
                AssertTrue(FurimaServerNative.MatchesIfNoneMatch("*", etag, h), "Wildcard failed");
                AssertTrue(!FurimaServerNative.MatchesIfNoneMatch("\"different\"", etag, h), "Non-matching ETag matched");
            });

            RunTest("FurimaServerNative.DetectImageExtension detects JPEG/PNG/GIF/WEBP magic bytes", () =>
            {
                AssertEquals(".png", FurimaServerNative.DetectImageExtension(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "PNG detection failed");
                AssertEquals(".jpg", FurimaServerNative.DetectImageExtension(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }), "JPG detection failed");
                AssertEquals(".gif", FurimaServerNative.DetectImageExtension(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }), "GIF detection failed");
            });

            RunTest("FurimaServerNative.IsTrustedClaudeIp validates exact IP, IPv4-mapped IPv6, and rejects forwarded/untrusted", () =>
            {
                string trusted = "100.64.10.20";
                AssertTrue(FurimaServerNative.IsTrustedClaudeIp(IPAddress.Parse("100.64.10.20"), false, trusted), "Exact IPv4 should match");
                AssertTrue(FurimaServerNative.IsTrustedClaudeIp(IPAddress.Parse("::ffff:100.64.10.20"), false, trusted), "IPv4-mapped IPv6 should match");
                AssertTrue(!FurimaServerNative.IsTrustedClaudeIp(IPAddress.Parse("100.64.10.20"), true, trusted), "Forwarded header must be rejected");
                AssertTrue(!FurimaServerNative.IsTrustedClaudeIp(IPAddress.Parse("100.64.10.21"), false, trusted), "Different IP must be rejected");
                AssertTrue(!FurimaServerNative.IsTrustedClaudeIp(IPAddress.Parse("100.64.10.20"), false, ""), "Empty config must be rejected");
            });

            RunTest("SyncStore persists lastClaudePatrolContactAt across store.json reload and SaveDefaultSettings", () =>
            {
                string baseDir = Path.Combine(tempRoot, "sync_patrol_contact");
                Directory.CreateDirectory(baseDir);
                string token = "toktest123";
                SyncStore.Initialize(baseDir, ref token);
                AssertTrue(SyncStore.SetTrustedMuseIp("100.64.1.50"), "SetTrustedMuseIp failed");
                long ts = 1790000123456L;
                SyncStore.SetLastClaudePatrolContactAt(ts, true);
                AssertTrue(SyncStore.GetLastClaudePatrolContactAt() == ts, "Patrol contact timestamp mismatch in memory");

                var defaults = SyncStore.GetDefaultSettings();
                SyncStore.SaveDefaultSettings(defaults);

                string token2 = "";
                SyncStore.Initialize(baseDir, ref token2);
                AssertEquals("100.64.1.50", SyncStore.GetTrustedMuseIp(), "trustedMuseIp lost on reload");
                AssertTrue(SyncStore.GetLastClaudePatrolContactAt() == ts, "lastClaudePatrolContactAt lost on reload");
            });

            RunTest("ClaudeJobStore.WakeOnStartupIfExecutable bumps revision only when approved/processing jobs exist", () =>
            {
                string syncDir = Path.Combine(tempRoot, "wake_startup_test");
                Directory.CreateDirectory(syncDir);
                SyncStore.SetSyncDirForTest(syncDir);
                ClaudeJobStore.ResetCacheForTest();

                var waitingJob = new ClaudeJob
                {
                    itemId = 1,
                    itemUuid = "uuid-wait",
                    itemTitle = "wait item",
                    queue = "waiting",
                    templateId = "t1",
                    templateName = "tmpl",
                    photos = new List<ClaudeJobPhoto> { new ClaudeJobPhoto { md5 = "0123456789abcdef0123456789abcdef", fileName = "a.jpg" } }
                };
                ClaudeJobStore.CreateJob(waitingJob);
                long revBefore = ClaudeJobStore.GetRevision();
                AssertTrue(!ClaudeJobStore.WakeOnStartupIfExecutable(), "Waiting job alone should not trigger startup wake");
                AssertTrue(ClaudeJobStore.GetRevision() == revBefore, "Revision should not change when no executable jobs");

                var patrolJob = new ClaudeJob
                {
                    itemId = 2,
                    itemUuid = "uuid-app",
                    itemTitle = "approved item",
                    queue = "patrol",
                    templateId = "t1",
                    templateName = "tmpl",
                    photos = new List<ClaudeJobPhoto> { new ClaudeJobPhoto { md5 = "abcdef0123456789abcdef0123456789", fileName = "b.jpg" } }
                };
                var createdPatrol = ClaudeJobStore.CreateJob(patrolJob);
                ClaudeJobStore.MutateJob(createdPatrol.jobId, j =>
                {
                    j.status = "approved";
                    j.jobToken = "tok-keep";
                    return true;
                });
                long revAfterApprove = ClaudeJobStore.GetRevision();
                ClaudeJobStore.ResetCacheForTest();

                AssertTrue(ClaudeJobStore.WakeOnStartupIfExecutable(), "Approved job should trigger startup wake");
                AssertTrue(ClaudeJobStore.GetRevision() == revAfterApprove + 1, "Revision should increment by 1 on startup wake");
                var reloaded = ClaudeJobStore.GetJob(createdPatrol.jobId);
                AssertEquals("approved", reloaded.status, "Startup wake must not mutate job status");
                AssertEquals("tok-keep", reloaded.jobToken, "Startup wake must not mutate jobToken");
            });

            RunTest("P3 Plan B: approval_pending and approved never auto-drop to waiting; expired processing stops as done_with_errors", () =>
            {
                string syncDir = Path.Combine(tempRoot, "p3_plan_b_test");
                Directory.CreateDirectory(syncDir);
                SyncStore.SetSyncDirForTest(syncDir);
                ClaudeJobStore.ResetCacheForTest();

                long oldTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (120L * 60L * 1000L);

                var pendingJob = ClaudeJobStore.CreateJob(new ClaudeJob
                {
                    itemId = 10,
                    itemUuid = "uuid-p",
                    itemTitle = "pending item",
                    queue = "patrol",
                    templateId = "t1",
                    templateName = "tmpl",
                    photos = new List<ClaudeJobPhoto> { new ClaudeJobPhoto { md5 = "11111111111111111111111111111111", fileName = "p.jpg" } }
                });
                ClaudeJobStore.MutateJob(pendingJob.jobId, j => { j.approvalRequestedAt = oldTs; return true; });
                AssertTrue(!FurimaServerNative.ApplyApprovalPendingExpiry(ClaudeJobStore.GetJob(pendingJob.jobId)), "approval_pending should not auto-expire");
                AssertEquals("approval_pending", ClaudeJobStore.GetJob(pendingJob.jobId).status, "approval_pending should remain approval_pending");

                var approvedJob = ClaudeJobStore.CreateJob(new ClaudeJob
                {
                    itemId = 11,
                    itemUuid = "uuid-a",
                    itemTitle = "approved item",
                    queue = "patrol",
                    templateId = "t1",
                    templateName = "tmpl",
                    photos = new List<ClaudeJobPhoto> { new ClaudeJobPhoto { md5 = "22222222222222222222222222222222", fileName = "a.jpg" } }
                });
                ClaudeJobStore.MutateJob(approvedJob.jobId, j =>
                {
                    j.status = "approved";
                    j.jobToken = "tok-app";
                    j.jobTokenExpiresAt = oldTs;
                    return true;
                });
                AssertTrue(!FurimaServerNative.ApplyTokenExpiry(ClaudeJobStore.GetJob(approvedJob.jobId)), "approved should not expire or drop to waiting");
                AssertEquals("approved", ClaudeJobStore.GetJob(approvedJob.jobId).status, "approved must remain approved");

                var procJob = ClaudeJobStore.CreateJob(new ClaudeJob
                {
                    itemId = 12,
                    itemUuid = "uuid-proc",
                    itemTitle = "processing item",
                    queue = "patrol",
                    templateId = "t1",
                    templateName = "tmpl",
                    photos = new List<ClaudeJobPhoto> { new ClaudeJobPhoto { md5 = "33333333333333333333333333333333", fileName = "proc.jpg" } }
                });
                ClaudeJobStore.MutateJob(procJob.jobId, j =>
                {
                    j.status = "processing";
                    j.jobToken = "tok-proc";
                    j.jobTokenExpiresAt = oldTs;
                    return true;
                });
                AssertTrue(FurimaServerNative.ApplyTokenExpiry(ClaudeJobStore.GetJob(procJob.jobId)), "expired processing should terminate");
                var afterProc = ClaudeJobStore.GetJob(procJob.jobId);
                AssertEquals("done_with_errors", afterProc.status, "expired processing with 0 results must become done_with_errors, never waiting");
                AssertTrue(afterProc.errors != null && afterProc.errors.Count == 1, "expired processing should record timeout error");
                AssertTrue(string.IsNullOrEmpty(afterProc.jobToken), "jobToken must be cleared on expiry termination");
            });

            RunTest("ClaudeJobStore.CountExecutableJobs counts approved and processing only", () =>
            {
                string syncDir = Path.Combine(tempRoot, "exec_count_test");
                Directory.CreateDirectory(syncDir);
                SyncStore.SetSyncDirForTest(syncDir);
                ClaudeJobStore.ResetCacheForTest();

                var j1 = ClaudeJobStore.CreateJob(new ClaudeJob { itemId = 1, itemUuid = "u1", itemTitle = "w", queue = "waiting", templateId = "t1", templateName = "t" });
                var j2 = ClaudeJobStore.CreateJob(new ClaudeJob { itemId = 2, itemUuid = "u2", itemTitle = "p", queue = "patrol", templateId = "t1", templateName = "t" });
                var j3 = ClaudeJobStore.CreateJob(new ClaudeJob { itemId = 3, itemUuid = "u3", itemTitle = "a", queue = "patrol", templateId = "t1", templateName = "t" });
                ClaudeJobStore.MutateJob(j2.jobId, j => { j.status = "processing"; return true; });
                ClaudeJobStore.MutateJob(j3.jobId, j => { j.status = "approved"; return true; });
                AssertTrue(ClaudeJobStore.CountExecutableJobs() == 2, "Expected 2 executable jobs, got " + ClaudeJobStore.CountExecutableJobs());
            });
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }

        Console.WriteLine(string.Format("C# Tests: {0} passed, {1} failed", passed, failed));
        return failed == 0 ? 0 : 1;
    }
}
