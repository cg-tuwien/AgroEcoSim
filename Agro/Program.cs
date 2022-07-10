using System;
using Agro;

internal class Program
{
    private static void Main(string[] args)
    {
        var world = Initialize.World();

        var start = DateTime.UtcNow.Ticks;
        world.Run(AgroWorld.TimestepsTotal);
        var stop = DateTime.UtcNow.Ticks;
        Console.WriteLine($"Simulation time: {(stop - start) / TimeSpan.TicksPerMillisecond} ms");
    }
}