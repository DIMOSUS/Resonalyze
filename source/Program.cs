using NAudio.Wave;

namespace Resonalyze
{
    internal static class Program
    {
        // When the capturer receives audio, start writing the buffer into the mentioned file
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            //WasapiLoopbackCapture CaptureInstance = new WasapiLoopbackCapture();

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}
