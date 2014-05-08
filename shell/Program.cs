using System;
using System.Configuration;

namespace TeamCityConfigBuilder.Shell
{
    class Program
    {
        static void Main()
        {
            Library.Builder.Run(
                ConfigurationManager.AppSettings.Get("DiscoveryFolder"),
                ConfigurationManager.AppSettings.Get("TemplateFolder"),
                ConfigurationManager.AppSettings.Get("TeamCityUrl"),
                ConfigurationManager.AppSettings.Get("TeamCityUsername"),
                ConfigurationManager.AppSettings.Get("TeamCityPassword"),
                new ConsoleMessageObserver());
            Console.WriteLine("press any key to exit");
            Console.ReadKey();
        }
    }
}
