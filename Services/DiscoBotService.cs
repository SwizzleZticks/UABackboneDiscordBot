using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace UABackoneBot.Services
{
    public static class DiscoBotService
    {
        private static DiscordSocketClient? _client;
        private static InteractionService? _interactions;
        private static JobSyncService? _jobSyncService;

        public static async Task StartAsync()
        {
            var token = Environment.GetEnvironmentVariable("BOT_KEY");
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents =
                    GatewayIntents.Guilds |
                    GatewayIntents.GuildMessages |
                    GatewayIntents.GuildMessageReactions |
                    GatewayIntents.MessageContent
            });

            _interactions = new InteractionService(_client);

            _client.Log += Log;
            _interactions.Log += Log;

            _client.Ready += OnReady;
            _client.InteractionCreated += OnInteractionCreated;

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
        }

        private static async Task OnReady()
        {
            ulong devGuildId = 1403732519664226334;

            await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), null);
            await _interactions.RegisterCommandsToGuildAsync(devGuildId);
            _jobSyncService = new JobSyncService(_client, new CsvDownloaderService(), new CsvConverterService(), 1403872991422578790);
            _jobSyncService.LogCurrentStatus += HandleJobSyncStatus;
            _jobSyncService.Start();
        }

        private static async Task OnInteractionCreated(SocketInteraction interaction)
        {
            var ctx = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(ctx, null);
        }

        private static Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private static Task HandleJobSyncStatus(string msg)
        {
            Console.WriteLine(msg);
            return Task.CompletedTask;
        }
    }

}
