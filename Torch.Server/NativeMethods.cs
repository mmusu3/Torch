using System.Runtime.InteropServices;

namespace Torch.Server;

public static class NativeMethods
{
    [DllImport("kernel32")]
    public static extern bool AllocConsole();

    [DllImport("kernel32")]
    public static extern nint GetConsoleWindow();

    [DllImport("kernel32")]
    public static extern bool FreeConsole();
}
