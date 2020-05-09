using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

class Utility
{
    public static void decryptSave(string gameid, string saveDir)
    {
        // update PFD
        System.Diagnostics.ProcessStartInfo procStartInfo = new System.Diagnostics.ProcessStartInfo("pfdtool\\pfdtool.exe", "-g " + gameid + " -d \"" + saveDir + "\" USR-DATA");
        procStartInfo.RedirectStandardOutput = true;
        procStartInfo.UseShellExecute = false;
        procStartInfo.CreateNoWindow = true;
        System.Diagnostics.Process proc = new System.Diagnostics.Process();
        proc.StartInfo = procStartInfo;
        proc.Start();
        proc.WaitForExit();
    }

    public static void encryptSave(string gameid, string saveDir)
    {
        // update PFD
        System.Diagnostics.ProcessStartInfo procStartInfo = new System.Diagnostics.ProcessStartInfo("pfdtool\\pfdtool.exe", "-g " + gameid + " -u \"" + saveDir + "\"");
        procStartInfo.RedirectStandardOutput = true;
        procStartInfo.UseShellExecute = false;
        procStartInfo.CreateNoWindow = true;
        System.Diagnostics.Process proc = new System.Diagnostics.Process();
        proc.StartInfo = procStartInfo;
        proc.Start();
        proc.WaitForExit();
        // encrypt savedata
        procStartInfo = new System.Diagnostics.ProcessStartInfo("pfdtool\\pfdtool.exe", "-g " + gameid + " -e \"" + saveDir + "\" USR-DATA");
        procStartInfo.RedirectStandardOutput = true;
        procStartInfo.UseShellExecute = false;
        procStartInfo.CreateNoWindow = true;
        proc = new System.Diagnostics.Process();
        proc.StartInfo = procStartInfo;
        proc.Start();
        proc.WaitForExit();
    }

    public static void decryptTrophy(string saveDir)
    {
        // update PFD
        System.Diagnostics.ProcessStartInfo procStartInfo = new System.Diagnostics.ProcessStartInfo("pfdtool\\pfdtool.exe", " -d \"" + saveDir + "\" TROPTRNS.DAT");
        procStartInfo.WorkingDirectory = "pfdtool";
        procStartInfo.RedirectStandardOutput = true;
        procStartInfo.UseShellExecute = false;
        procStartInfo.CreateNoWindow = true;
        System.Diagnostics.Process proc = new System.Diagnostics.Process();
        proc.StartInfo = procStartInfo;
        proc.Start();
        proc.WaitForExit();
    }

    public static void encryptTrophy(string saveDir)
    {
        // update PFD
        System.Diagnostics.ProcessStartInfo procStartInfo = new System.Diagnostics.ProcessStartInfo("pfdtool\\pfdtool.exe", " -u \"" + saveDir + "\"");
        procStartInfo.WorkingDirectory = "pfdtool";
        procStartInfo.RedirectStandardOutput = true;
        procStartInfo.UseShellExecute = false;
        procStartInfo.CreateNoWindow = true;
        System.Diagnostics.Process proc = new System.Diagnostics.Process();
        proc.StartInfo = procStartInfo;
        proc.Start();
        proc.WaitForExit();
        // encrypt savedata
        procStartInfo = new System.Diagnostics.ProcessStartInfo("pfdtool\\pfdtool.exe", " -e \"" + saveDir + "\" TROPTRNS.DAT");
        procStartInfo.WorkingDirectory = "pfdtool";
        procStartInfo.RedirectStandardOutput = true;
        procStartInfo.UseShellExecute = false;
        procStartInfo.CreateNoWindow = true;
        proc = new System.Diagnostics.Process();
        proc.StartInfo = procStartInfo;
        proc.Start();
        proc.WaitForExit();
    }

    public static long LongRandom(long min, long max, Random rand)
    {
        byte[] buf = new byte[8];
        rand.NextBytes(buf);
        long longRand = BitConverter.ToInt64(buf, 0);

        return (Math.Abs(longRand % (max - min)) + min);
    }
    public static DateTime TimeStampToDateTime(long timestamp)
    {
        DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0,DateTimeKind.Utc);
        dateTime = dateTime.AddSeconds(timestamp);
        return dateTime;
    }
}
