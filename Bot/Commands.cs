﻿using Discord;
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
        private DataStore dataStore;
        private ISocketMessageChannel channel;
        private SocketUser lobbyCreator;
        private SocketVoiceChannel voiceChannel;

        public Commands()
        {
            dataStore = new DataStore();
        }
        

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
            // don't set if names are the same
            if (inGameName == Context.Message.Author.Username)
            {
                return ReplyAsync($"{Context.Message.Author.Mention} since your name is the same as in game, there is no need to set it.");
            } else
            {
                Member member = dataStore.members.Find(Context.Message.Author.Id);
                if (member != null)
                {
                    member.amongUsName = inGameName;
                } else
                {
                    dataStore.Add(new Member { amongUsName = inGameName, discordId = Context.Message.Author.Id });
                }
                dataStore.SaveChanges();
                return ReplyAsync($"{Context.Message.Author.Mention} set `{inGameName}` as your in game name.");
            }
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
                    //TODO: will act odd if a lot of people have the same name
                    var member = dataStore.members.First(member => member.amongUsName.Equals(player.Key, StringComparison.OrdinalIgnoreCase));
                    if (member != null)
                    {
                        muteUser(voiceChannel.Users.Where(x => x.Id == member.discordId).First(), false);
                    }
                    else
                    {
                        IEnumerable<SocketGuildUser> user = voiceChannel.Users.Where((x) =>
                        {
                            return x.Username.Equals(player.Key, StringComparison.OrdinalIgnoreCase);
                        });
                        if (user.Count() == 1)
                        {
                            muteUser(user.First(), false);
                        }
                    }
                }
            }
            else if (e.NewState == GameState.TASKS)
            {
                muteUsers(voiceChannel.Users, true);
            }
            else if (e.NewState == GameState.LOBBY)
            {
                muteUsers(voiceChannel.Users, false);
            }
        }

        /// <summary>
        /// Change mute on a list of users
        /// </summary>
        /// <param name="users">users to modify</param>
        /// <param name="mute">True to mute the user or False to unmute them</param>
        private void muteUsers(IReadOnlyCollection<SocketGuildUser> users, bool mute)
        {
            foreach (var user in users)
            {
                muteUser(user, mute);
            }
        }

        /// <summary>
        /// Change the mute setting on a user
        /// </summary>
        /// <param name="socketGuildUser">User</param>
        /// <param name="mute">True to mute the user or False to unmute them</param>
        private void muteUser(SocketGuildUser socketGuildUser, bool mute)
        {
            socketGuildUser.ModifyAsync(x =>
            {
                x.Mute = mute;
            });
        }


    }
}
