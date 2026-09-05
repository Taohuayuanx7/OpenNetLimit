using System.Runtime.InteropServices;
using System.Windows;

namespace UdpTest;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--headless")
        {
            return Headless.Run(args);
        }

        if (args.Length > 0 && args[0] == "--rate")
        {
            return Headless.Rate(args);
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }
}