using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Reflection;
using System.Threading;

namespace UABackoneBot.Services
{
    public static class DiscoBotService
    {
        private static DiscordSocketClient? _client;
        private static InteractionService? _interactions;
        private static JobSyncService? _jobSyncService;

        private static int _readyOnce = 0;

        public static async Task StartAsync(CancellationToken ct = default)
        {
            var token = Environment.GetEnvironmentVariable("BOT_KEY");
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("BOT_KEY env var is missing.");

            var attempt = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    attempt++;
                    await StartClientOnceAsync(token);

                    // Keep process alive; if anything fatal bubbles up, we'll restart.
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break; // normal shutdown
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Gateway] Exception: {ex}");
                    await RestartBackoffAsync(attempt, ct);
                }
                finally
                {
                    await SafeDisposeAsync();
                    Interlocked.Exchange(ref _readyOnce, 0);
                }
            }
        }

        private static async Task StartClientOnceAsync(string token)
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents =
                    GatewayIntents.Guilds |
                    GatewayIntents.GuildMessages |
                    GatewayIntents.GuildMessageReactions,

                AlwaysDownloadUsers = false,
                MessageCacheSize = 0,
                LogGatewayIntentWarnings = false
            });

            _interactions = new InteractionService(_client);

            _client.Log += Log;
            _interactions.Log += Log;

            _client.Ready += OnReady;
            _client.InteractionCreated += OnInteractionCreated;
            _client.Disconnected += OnDisconnected;
            _client.Connected += () =>
            {
                Console.WriteLine("[Gateway] Connected.");
                return Task.CompletedTask;
            };

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
        }

        private static async Task OnReady()
        {
            if (Interlocked.Exchange(ref _readyOnce, 1) == 1)
                return;

            Console.WriteLine("[Gateway] Ready.");

            ulong devGuildId = 1403732519664226334;

            await _interactions!.AddModulesAsync(Assembly.GetExecutingAssembly(), services: null);
            await _interactions.RegisterCommandsToGuildAsync(devGuildId);

            _jobSyncService = new JobSyncService(
                _client!,
                new CsvDownloaderService(),
                new CsvConverterService(),
                channelId: 1403872991422578790);

            _jobSyncService.LogCurrentStatus += HandleJobSyncStatus;
            _jobSyncService.Start();
        }

        private static async Task OnInteractionCreated(SocketInteraction interaction)
        {
            try
            {
                var ctx = new SocketInteractionContext(_client!, interaction);
                var result = await _interactions!.ExecuteCommandAsync(ctx, services: null);

                if (!result.IsSuccess)
                    Console.WriteLine($"[Interactions] {result.Error}: {result.ErrorReason}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Interactions] Exception: {ex}");
            }
        }

        private static Task OnDisconnected(Exception? ex)
        {
            Console.WriteLine(ex is null
                ? "[Gateway] Disconnected."
                : $"[Gateway] Disconnected: {ex.GetType().Name} - {ex.Message}");

            return Task.CompletedTask;
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

        private static async Task RestartBackoffAsync(int attempt, CancellationToken ct)
        {
            var seconds = Math.Min(120, (int)Math.Pow(2, Math.Min(attempt, 7))); // 2..120
            var jitterMs = Random.Shared.Next(0, 750);
            var delay = TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(jitterMs);

            Console.WriteLine($"[Gateway] Restarting in {delay.TotalSeconds:F1}s (attempt {attempt}).");
            await Task.Delay(delay, ct);
        }

        private static async Task SafeDisposeAsync()
        {
            try
            {
                if (_jobSyncService != null)
                {
                    await _jobSyncService.StopAsync();
                    _jobSyncService.LogCurrentStatus -= HandleJobSyncStatus;
                }
            }
            catch { /* ignore */ }
            finally
            {
                _jobSyncService = null;
            }

            try
            {
                if (_client != null)
                {
                    await _client.StopAsync();
                    await _client.LogoutAsync();
                    _client.Dispose();
                }
            }
            catch { /* ignore */ }

            _client = null;
            _interactions = null;
        }
    }
}
