using Discord;
using Discord.Commands;
using Discord.WebSocket;
using SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Bot
{
    public class Commands : ModuleBase<SocketCommandContext>
    {

        private ISocketMessageChannel channel;
        private SocketUser lobbyCreator;
        private SocketVoiceChannel voiceChannel;
        private static Dictionary<String, ulong> discordAmongUsName = new Dictionary<string, ulong>();

        [Command("connect")]
        [Alias("start", "play")]
        public Task PingAsync()
        {

            var voiceChannels = Context.Guild.VoiceChannels.Where(channel => channel.Users.Any(a => a.Id.Equals(Context.Message.Author.Id)));

            if (voiceChannels.Count() != 1)
                return ReplyAsync($"{Context.Message.Author.Mention} You must be connected to a voice channel before starting a game");
            
            ReplyAsync("connecting to local among us instance...");

            Task.Factory.StartNew(() => AmongUsReader.getInstance().Run());
            AmongUsReader.getInstance().GameStateChanged += GameStateChangedHandler;
            AmongUsReader.getInstance().PlayerChanged += PlayerChangedHandler;
            AmongUsReader.getInstance().JoinedLobby += JoinedLobbyHandler;
            AmongUsReader.getInstance().ChatMessageAdded += ChatMessageHandler;

            channel = Context.Channel;
            lobbyCreator = Context.User;
            voiceChannel = voiceChannels.First();


            return ReplyAsync("Sucessfully connected to local among us instance. Use `set-name {name}` to set your in game name if it is different then your discord name");
        }

        [Command("set-name")]
        public Task PingAsync(String inGameName)
        {
            discordAmongUsName.Add(inGameName.ToLower(), Context.Message.Author.Id);
            return ReplyAsync($"{Context.Message.Author.Mention} set `{inGameName}` as your in game name.");
        }

        private void ChatMessageHandler(object sender, ChatMessageEventArgs e)
        {
            
        }

        private void JoinedLobbyHandler(object sender, LobbyEventArgs e)
        {
            var embed = new EmbedBuilder()
            .WithTitle("New Lobby Created")
            .WithImageUrl("https://cdn.discordapp.com/avatars/770073344468713572/3b7966fccd41b4572839a49db914508b.png")
            .WithFooter(footer => footer.Text = "AmongUs Moderator")
            .WithColor(Color.Blue)
            .WithCurrentTimestamp()
            .AddField("Lobby Code", e.LobbyCode)
            .AddField("Region", e.Region);

            channel.SendMessageAsync(embed: embed.Build());
        }

        private void PlayerChangedHandler(object sender, PlayerChangedEventArgs e)
        {


        }

        private void GameStateChangedHandler(object sender, GameStateChangedEventArgs e)
        {
            if (e.NewState == GameState.DISCUSSION)
            {
                var alivePlayers = AmongUsReader.getInstance().GetAlivePlayers();
                foreach (var player in alivePlayers)
                {
                    ulong userId;
                    if (discordAmongUsName.TryGetValue(player.Key, out userId))
                    {
                        voiceChannel.Users.Where(x => x.Id == userId).First().ModifyAsync(x =>
                        {
                            x.Mute = false;
                        });
                    } else
                    {
                        IEnumerable<SocketGuildUser> user = voiceChannel.Users.Where(x => x.Nickname.ToLower() == player.Key); 
                        if (user.Count() == 1)
                        {
                            user.First().ModifyAsync(x =>
                            {
                                x.Mute = false;
                            });
                        }
                    }
                }

            } else if (e.NewState == GameState.TASKS)
            {
                foreach (var user in voiceChannel.Users)
                {
                    user.ModifyAsync(x =>
                    {
                        x.Mute = true;
                    });
                }
            }
        }
    }
}
