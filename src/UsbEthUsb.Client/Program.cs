namespace UsbEthUsb.Client;

internal static class Program
{
    // STA is required for the Vista-style file dialogs (IFileDialog COM). Without it,
    // OpenFileDialog.ShowDialog hangs forever on the COM activation. Top-level statements
    // do not auto-apply this attribute even when UseWindowsForms is set.
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAppContext());
    }
}
