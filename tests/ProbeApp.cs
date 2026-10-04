using System;
using System.IO;
using System.Threading;
public static class ProbeApp
{
    public static int Main(string[] args)
    {
        File.WriteAllText(args[0] + ".started", args.Length > 1 ? args[1] : "probe");
        Thread.Sleep(3500);
        File.WriteAllText(args[0] + ".finished", "finished");
        return 0;
    }
}
