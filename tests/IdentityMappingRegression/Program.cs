using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using PS3TrophyIsGood;

internal static class Program
{
    private sealed class RemoteFixture
    {
        internal readonly string Name;
        internal readonly string Detail;
        internal readonly string Type;
        internal readonly long Date;

        internal RemoteFixture(string name, string detail, string type, long date)
        {
            Name = name;
            Detail = detail;
            Type = type;
            Date = date;
        }
    }

    private static int passed;

    [STAThread]
    private static int Main()
    {
        try
        {
            // The PS3 game can call the same trophy The Artisan or The Craftsman.
            AssertMapped("Renamed trophy (PSNProfiles)",
                new[] { Local(12, "The Artisan", "Combined two materials to make an item.", "B") },
                new[] { Remote("The Craftsman", "Combined two materials to make an item.", "B", 1234) },
                12, 1234, "PsnProfiles");

            AssertMapped("Renamed trophy (PSN Trophy Leaders)",
                new[] { Local(12, "The Craftsman", "Combined two materials to make an item.", "B") },
                new[] { Remote("The Artisan", "Combined two materials to make an item.", "B", 4567) },
                12, 4567, "PsnTrophyLeaders");

            AssertMapped("Normalized description suffix",
                new[] { Local(12, "The Artisan", "Combined two materials to make an item. (Hidden)", "B") },
                new[] { Remote("The Craftsman", "Combined two materials to make an item.", "B", 1234) },
                12, 1234, "PsnProfiles");

            AssertMapped("Exact title unaffected",
                new[] { Local(4, "The Veteran", "Defeated 3,000 enemies.", "B") },
                new[] { Remote("The Veteran", "Defeated 3,000 enemies.", "B", 8675) },
                4, 8675, "PsnProfiles");

            AssertMapped("Repeated hyphens normalize to a single space",
                new[] { Local(5, "Adrenaline--Junkie", "Test description.", "B") },
                new[] { Remote("Adrenaline Junkie", "Different description.", "B", 222) },
                5, 222, "PsnProfiles");

            AssertMapped("Multi-trophy mapping keeps local IDs",
                new[]
                {
                    Local(8, "The Artisan", "Combined two materials to make an item.", "B"),
                    Local(4, "The Veteran", "Defeated 3,000 enemies.", "B")
                },
                new[]
                {
                    Remote("The Veteran", "Defeated 3,000 enemies.", "B", 111),
                    Remote("The Craftsman", "Combined two materials to make an item.", "B", 222)
                },
                new[] { 4, 8 }, new long[] { 111, 222 }, "PsnProfiles");

            AssertRejected("Different descriptions are unsafe",
                new[] { Local(12, "The Artisan", "Combined two materials to make an item.", "B") },
                new[] { Remote("The Craftsman", "Unrelated action.", "B", 1234) });

            AssertRejected("Renamed trophy with unknown grade is unsafe",
                new[] { Local(12, "The Artisan", "Combined two materials to make an item.", "B") },
                new[] { Remote("The Craftsman", "Combined two materials to make an item.", "", 1234) });

            AssertRejected("Matching name but different grade is unsafe",
                new[] { Local(12, "The Craftsman", "Combined two materials to make an item.", "B") },
                new[] { Remote("The Craftsman", "Combined two materials to make an item.", "G", 1234) });

            AssertRejected("Matching description but different grade is unsafe",
                new[] { Local(12, "The Artisan", "Combined two materials to make an item.", "B") },
                new[] { Remote("The Craftsman", "Combined two materials to make an item.", "G", 1234) });

            AssertRejected("Blank description cannot match renamed trophy",
                new[] { Local(12, "The Artisan", "", "B") },
                new[] { Remote("The Craftsman", "", "B", 1234) });

            AssertRejected("Non-unique description cannot match renamed trophy",
                new[]
                {
                    Local(12, "The Artisan", "Combined two materials to make an item.", "B"),
                    Local(13, "Other Trophy", "Combined two materials to make an item.", "B")
                },
                new[] { Remote("The Craftsman", "Combined two materials to make an item.", "B", 1234) });

            AssertRejected("Two remote trophies cannot map to one local trophy",
                new[] { Local(12, "The Artisan", "Combined two materials to make an item.", "B") },
                new[]
                {
                    Remote("The Craftsman", "Combined two materials to make an item.", "B", 1234),
                    Remote("The Artisan", "Combined two materials to make an item.", "B", 5678)
                });

            AssertLocalizedPsntlSet("51 French trophies map to English XMB IDs", true);
            AssertLocalizedPsntlSet("Localized rows work in reverse page order", true, reverseRows: true);
            AssertLocalizedPsntlSet("Missing XMB ID must not be guessed", false, missingXmbId: 11);
            AssertLocalizedPsntlSet("Duplicate XMB IDs must not be guessed", false, duplicateXmbId: 11);
            AssertLocalizedPsntlSet("Different XMB grade must not be guessed", false, wrongTypeId: 11);
            AssertLocalizedPsntlSet("Conflicting matching name defeats XMB fallback", false, conflictAnchor: true);
            AssertLocalizedPsntlSet("Partial cross-language set must not assume ID", false, partialSet: true);

            Console.WriteLine("PASS: " + passed + " identity mapping regression tests.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL after " + passed + " tests: " + ex);
            return 1;
        }
    }

    // Exercise the production HTML parser AND the production identity mapper.
    // The XMB ID is intentionally different from the page title's ID.
    private static void AssertLocalizedPsntlSet(
        string name, bool shouldSucceed, bool reverseRows = false,
        int missingXmbId = -1, int duplicateXmbId = -1,
        int wrongTypeId = -1, bool conflictAnchor = false,
        bool partialSet = false)
    {
        var local = Enumerable.Range(0, 51).Select(id => Local(
            id,
            id == 7 ? "Shared Trophy Title" :
                id == 0 ? "Le véritable insurgé" : "Trophée français " + id,
            "Description française " + id + ".",
            Grade(id))).ToArray();

        IEnumerable<int> ordered = Enumerable.Range(0, partialSet ? 50 : 51);
        if (reverseRows)
            ordered = ordered.Reverse();

        var html = new StringBuilder();
        foreach (int id in ordered)
        {
            int xmbId = duplicateXmbId == id ? id - 1 :
                        conflictAnchor && id == 7 ? 8 : id;
            string nameEn = id == 7 ? "Shared Trophy Title" :
                id == 0 ? "The True Arisen" : "English Trophy " + id;
            string grade = wrongTypeId == id ? "G" : Grade(id);
            string typeCode = grade == "P" ? "3" :
                              grade == "G" ? "2" :
                              grade == "S" ? "1" : "0";
            html.Append("<tr class=\"trophyvalue").Append(id).Append("\">");
            if (id != missingXmbId)
                html.Append("<td class=\"XMB\">").Append(xmbId).Append("</td>");
            html.Append("<td><a id=\"trophytitle").Append(500 + id).Append("\">")
                .Append(nameEn).Append("</a>");
            html.Append("<div id=\"trophydescription").Append(id)
                .Append("\">English description ").Append(id).Append(".</div></td>");
            html.Append("<td class=\"trophytype\">").Append(typeCode).Append("</td>");
            html.Append("<td class=\"date_earned\"><span class=\"sort\">")
                .Append(1700000000L + id).Append("</span></td></tr>");
        }

        try
        {
            using (var dialog = new CopyFrom())
            {
                dialog.SetLocalTrophies(local);
                MethodInfo parse = typeof(CopyFrom).GetMethod("ParsePsntlIdentities",
                    BindingFlags.NonPublic | BindingFlags.Static);
                MethodInfo map = typeof(CopyFrom).GetMethod("MapRemoteToLocal",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Type siteType = typeof(CopyFrom).GetNestedType(
                    "IdentitySource", BindingFlags.NonPublic);
                if (parse == null || map == null || siteType == null)
                    throw new Exception("Could not find production parser / mapper.");

                IList remotes;
                try
                {
                    remotes = (IList)parse.Invoke(null, new object[] { html.ToString(), 200 });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
                List<CopyFrom.Pair> result;
                try
                {
                    result = (List<CopyFrom.Pair>)map.Invoke(dialog,
                        new object[] { remotes, Enum.Parse(siteType, "PsnTrophyLeaders") });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }

                if (!shouldSucceed)
                    throw new Exception(name + ": unsafe mapping was accepted.");
                if (result.Count != 51)
                    throw new Exception(name + ": expected 51 rows, got " + result.Count);
                for (int id = 0; id < 51; ++id)
                {
                    if (result[id].Id != id || result[id].Date != 1700000000L + id)
                        throw new Exception(name + ": wrong XMB ID/timestamp at " + id);
                }
            }
        }
        catch (InvalidOperationException)
        {
            if (shouldSucceed)
                throw;
            Console.WriteLine("PASS: " + name);
            passed++;
            return;
        }

        if (!shouldSucceed)
            throw new Exception(name + ": expected a fail-closed rejection.");
        Console.WriteLine("PASS: " + name);
        passed++;
    }

    private static string Grade(int id)
    {
        if (id == 0) return "P";
        if (id == 10 || id == 22 || id == 39) return "G";
        if (id == 9 || id == 14 || id == 31 || id == 45) return "S";
        return "B";
    }

    private static CopyFrom.LocalTrophyIdentity Local(int id, string name, string detail, string grade)
    {
        return new CopyFrom.LocalTrophyIdentity
        {
            Id = id,
            Name = name,
            Detail = detail,
            Type = grade
        };
    }

    private static RemoteFixture Remote(string name, string detail, string grade, long date)
    {
        return new RemoteFixture(name, detail, grade, date);
    }

    private static List<CopyFrom.Pair> Map(
        CopyFrom.LocalTrophyIdentity[] local, RemoteFixture[] remote, string site)
    {
        using (var dialog = new CopyFrom())
        {
            dialog.SetLocalTrophies(local);

            Type remoteType = typeof(CopyFrom).GetNestedType("RemoteTrophyIdentity", BindingFlags.NonPublic);
            Type siteType = typeof(CopyFrom).GetNestedType("IdentitySource", BindingFlags.NonPublic);
            if (remoteType == null || siteType == null)
                throw new Exception("Could not locate production identity types.");

            IList remoteList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(remoteType));
            foreach (RemoteFixture fixture in remote)
            {
                object item = Activator.CreateInstance(remoteType, true);
                remoteType.GetProperty("RemoteId").SetValue(item, remoteList.Count + 1, null);
                remoteType.GetProperty("Name").SetValue(item, fixture.Name, null);
                remoteType.GetProperty("Detail").SetValue(item, fixture.Detail, null);
                remoteType.GetProperty("Type").SetValue(item, fixture.Type, null);
                remoteType.GetProperty("Date").SetValue(item, fixture.Date, null);
                remoteList.Add(item);
            }

            MethodInfo map = typeof(CopyFrom).GetMethod("MapRemoteToLocal",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (map == null)
                throw new Exception("Could not locate production MapRemoteToLocal.");
            try
            {
                return (List<CopyFrom.Pair>)map.Invoke(dialog,
                    new object[] { remoteList, Enum.Parse(siteType, site) });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }
    }

    private static void AssertMapped(
        string name, CopyFrom.LocalTrophyIdentity[] local, RemoteFixture[] remote,
        int expectedId, long expectedDate, string site)
    {
        AssertMapped(name, local, remote, new[] { expectedId }, new[] { expectedDate }, site);
    }

    private static void AssertMapped(
        string name, CopyFrom.LocalTrophyIdentity[] local, RemoteFixture[] remote,
        int[] expectedIds, long[] expectedDates, string site)
    {
        List<CopyFrom.Pair> result = Map(local, remote, site);
        if (result.Count != expectedIds.Length)
            throw new Exception(name + ": wrong match count " + result.Count);
        for (int i = 0; i < result.Count; ++i)
        {
            if (result[i].Id != expectedIds[i] || result[i].Date != expectedDates[i])
                throw new Exception(name + ": wrong mapped ID/timestamp at " + i);
        }
        Console.WriteLine("PASS: " + name);
        passed++;
    }

    private static void AssertRejected(
        string name, CopyFrom.LocalTrophyIdentity[] local, RemoteFixture[] remote)
    {
        try
        {
            Map(local, remote, "PsnProfiles");
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("PASS: " + name);
            passed++;
            return;
        }
        throw new Exception(name + ": an unsafe mapping was accepted.");
    }
}
