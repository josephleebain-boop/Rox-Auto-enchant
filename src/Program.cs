namespace RoxReroller;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); // screen coordinates must be real pixels
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var mutex = new Mutex(true, "RoxStatReroller.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("ROX Stat Reroller is already running.", "ROX Stat Reroller");
            return;
        }

        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.ToString(), "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);

        Application.Run(new MainForm());
    }
}
