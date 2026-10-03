namespace AOVPN.Cli;

public static class Program
{
    public static Task<int> Main(string[] args) => AOVPN.Controller.Program.Main(args);
}
