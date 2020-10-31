using System;
using System.Threading.Tasks;

public class Watcher
{
    public static void Main()
    {
        var path = @"C:\Users\Wouter\Documents\";
        //var path = @"\\192.168.1.100\Video\Arius";

        var k = new AriusCore.Arius(path);

        Task.Run(() => k.Monitor());

        // Wait for the user to quit the program.
        Console.WriteLine("Press 'q' to quit the sample.");
        while (Console.Read() != 'q') ;
    }
}