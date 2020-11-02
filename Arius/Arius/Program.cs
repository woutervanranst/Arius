using System;
using System.Threading.Tasks;

public class Watcher
{
    public static void Main()
    {
        var path = @"C:\Users\Wouter\Documents\";
        //var path = @"\\192.168.1.100\Video\Arius";

        var passphrase = "woutervr";

        // // DefaultEndpointsProtocol=https;AccountName=aurius;AccountKey=hKtsHebpvfQ9nk4UCImAgPY3Q1Pc8C2u4mFlXUCxGBkJF8Zu1ARJURjV39mymzPfpsyPeQpHAk66vy7Fs9kjvQ==;EndpointSuffix=core.windows.net

        var k = new AriusCore.Arius(path, passphrase, "aurius", "hKtsHebpvfQ9nk4UCImAgPY3Q1Pc8C2u4mFlXUCxGBkJF8Zu1ARJURjV39mymzPfpsyPeQpHAk66vy7Fs9kjvQ==");

        Task.Run(() => k.Monitor());

        // Wait for the user to quit the program.
        Console.WriteLine("Press 'q' to quit the sample.");
        while (Console.Read() != 'q') ;
    }
}