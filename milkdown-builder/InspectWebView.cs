using System;
using System.Reflection;
using Avalonia.Controls;

class Program
{
    static void Main()
    {
        foreach (var t in typeof(NativeWebView).Assembly.GetTypes())
        {
            if (t.Name.Contains("WindowsWebView2EnvironmentRequestedEventArgs"))
            {
                Console.WriteLine("Found: " + t.FullName);
                foreach (var p in t.GetProperties())
                {
                    Console.WriteLine("  Prop: " + p.Name + " (" + p.PropertyType.Name + ")");
                }
            }
        }
        foreach (var prop in typeof(NativeWebView).GetProperties())
        {
            Console.WriteLine("Property: " + prop.Name + " (" + prop.PropertyType.Name + ")");
        }
    }
}

