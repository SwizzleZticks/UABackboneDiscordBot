using System;
using System.Threading;
using System.Threading.Tasks;
using UABackoneBot.Services;

internal class Program
{
    static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("Shutting down...");
        };

        AppDomain.CurrentDomain.ProcessExit += (_, __) =>
        {
            if (!cts.IsCancellationRequested)
                cts.Cancel();
        };

        await DiscoBotService.StartAsync(cts.Token);
    }
}
