internal static class FakeSshHost
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "hang")
        {
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        if (args.Contains("-V"))
        {
            Console.Error.WriteLine("OpenSSH_9.5p1");
            return 0;
        }

        if (args.Contains("-G"))
        {
            Console.WriteLine("user git");
            Console.WriteLine("hostname example.test");
            Console.WriteLine("port 22");
            Console.WriteLine("identityfile /tmp/id_ed25519");
            Console.WriteLine("identityagent none");
            Console.WriteLine("proxyjump none");
            return 0;
        }

        if (args.Contains("-T"))
        {
            if (args.Contains("auth-fail"))
            {
                Console.Error.WriteLine("Permission denied (publickey).");
                return 255;
            }

            return 0;
        }

        var blob = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        Console.WriteLine("example.test ssh-ed25519 " + blob);
        return 0;
    }
}
