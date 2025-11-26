using UABackoneBot.Services;

namespace UABackoneBot
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            await DiscoBotService.StartAsync();
            await Task.Delay(-1);
        }
    }
}
