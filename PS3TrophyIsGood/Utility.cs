using System;
using System.IO;

public static class Utility
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
        string message = proc.StandardOutput.ReadToEnd();
        if (proc.ExitCode != 0)
        {
            throw new Exception("An error ocurred on decrypt trophies. Please check if Microsoft Visual C++ is installed. You can download it at: http://www.microsoft.com/download/en/details.aspx?id=5555");
        } else if (message != "pfdtool 0.2.3 (c) 2012 by flatz\r\n\r\n")
        {
            throw new Exception(message);
        }
    }

    public static void encryptTrophy(string saveDir, string profile)
    {
        //resing with other param.sfo
        if (profile != "Default Profile")
        {
            profile = "profiles\\" + profile;
            var br = new BinaryReader(new FileStream(profile, FileMode.Open));
            br.BaseStream.Position = 0xC;
            br.BaseStream.Position = br.ReadInt32();
            var profileId = br.ReadBytes(0x10);
            br.Close();
            var bw = new BinaryWriter(new FileStream(saveDir + "\\PARAM.SFO", FileMode.Open));
            bw.BaseStream.Position = 0x274;
            bw.Write(profileId);
            bw.Close();
        }
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
    public static DateTime TimeStampToDateTime(this long timestamp)
    {
        DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0,DateTimeKind.Utc);
        dateTime = dateTime.AddSeconds(timestamp);
        return dateTime;
    }
    public static long DateTimeToTimeStamp(this DateTime datetime)
    {
        DateTime sTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (long)(datetime - sTime).TotalSeconds;
    }

    public static string GetTemporaryDirectory()
    {
        string tempDirectory;
        do
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        } while (Directory.Exists(tempDirectory));
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    public static string CopyTrophyDirToTemp(string trophyDir)
    {
        DirectoryInfo dir = new DirectoryInfo(trophyDir);
        string pathTemp = Path.Combine(GetTemporaryDirectory(), dir.Name);
        DirectoryCopy(trophyDir, pathTemp, true);
        return pathTemp;
    }

    public static void DirectoryCopy(string source, string target, bool overwrite)
    {
        DirectoryInfo dir = new DirectoryInfo(source);
        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + source);
        }

        // If the destination directory doesn't exist, create it.
        Directory.CreateDirectory(target);

        // Get the files in the directory and copy them to the new location.
        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            string tempPath = Path.Combine(target, file.Name);
            file.CopyTo(tempPath, overwrite);
        }

        DirectoryInfo[] dirs = dir.GetDirectories();
        foreach (DirectoryInfo subdir in dirs)
        {
            string tempPath = Path.Combine(target, subdir.Name);
            DirectoryCopy(subdir.FullName, tempPath, overwrite);
        }
    }

    public static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
