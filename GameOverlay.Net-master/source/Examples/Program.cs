using System;
using System.Runtime.InteropServices;

namespace Examples
{
	public static class Program
	{
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        const int SW_MINIMIZE = 6;
        public static void Main(string[] _)
		{
            IntPtr handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SW_MINIMIZE);
            }
            GameOverlay.TimerService.EnableHighPrecisionTimers();

			using (var example = new Example())
			{
				example.Run();
			}
		}
	}
}
