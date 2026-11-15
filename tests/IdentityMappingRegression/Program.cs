using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

            Console.WriteLine("PASS: " + passed + " identity mapping regression tests.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL after " + passed + " tests: " + ex);
            return 1;
        }
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
