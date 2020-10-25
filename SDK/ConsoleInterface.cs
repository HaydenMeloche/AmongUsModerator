using System;
using System.Drawing;

namespace SDK
{
    public class ConsoleInterface
    {
        public void WriteLine(string str)
        {
            Console.WriteLine(str);
        }

        public void WriteLine(string ModuleName, string text)
        {
            Console.WriteLine($"[{ModuleName}]: {text}");
        }
    }
}
