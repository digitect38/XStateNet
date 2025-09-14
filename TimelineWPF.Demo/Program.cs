using System;
using System.Windows;

namespace TimelineWPF.Demo
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var app = new Application();
            var window = new DemoWindow();
            window.Title = "XStateNet Timeline Chart Demo";
            app.Run(window);
        }
    }
}