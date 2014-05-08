using System;
using TeamCityConfigBuilder.Library;

namespace TeamCityConfigBuilder.Shell
{
    public class ConsoleMessageObserver : IMessageObserver
    {
        public void Notify(string message)
        {
            Console.WriteLine(message);
        }

        public void Notify(string format, params object[] args)
        {
            Console.WriteLine(format, args);
        }
    }
}