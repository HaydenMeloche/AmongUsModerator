using Castle.DynamicProxy.Generators.Emitters.SimpleAST;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Xml.Serialization;

namespace SDK
{
    public class AmongUsReader
    {
        private static readonly AmongUsReader instance = new AmongUsReader();
        private bool exileCausesEnd;

        private bool shouldReadLobby = false;
        private IntPtr GameAssemblyPtr = IntPtr.Zero;

        private Dictionary<string, PlayerInfo> newPlayerInfos = new Dictionary<string, PlayerInfo>(10);

        private LobbyEventArgs latestLobbyEventArgs = null;

        private Dictionary<string, PlayerInfo> oldPlayerInfos = new Dictionary<string, PlayerInfo>(10);

        private GameState oldState = GameState.UNKNOWN;

        private int prevChatBubsVersion;
        private bool shouldForceTransmitState;
        private bool shouldForceUpdatePlayers;
        private bool shouldTransmitLobby;

        public event EventHandler<GameStateChangedEventArgs> GameStateChanged;

        public event EventHandler<PlayerChangedEventArgs> PlayerChanged;

        public event EventHandler<ChatMessageEventArgs> ChatMessageAdded;

        public event EventHandler<LobbyEventArgs> JoinedLobby;

        private bool foundModule = false;


        public void Run()
        {
            while (true)
            {
                if (!ProcessMemory.IsHooked)
                {
                    if (!ProcessMemory.HookProcess("Among Us"))
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    Console.WriteLine("GameMemReader", $"Connected to Among Us process ({ProcessMemory.process.Id}))");


                    // Register handlers for game-state change events.
                    //GameMemReader.getInstance().GameStateChanged += GameStateChangedHandler;
                    //GameMemReader.getInstance().PlayerChanged += PlayerChangedHandler;
                    //GameMemReader.getInstance().JoinedLobby += JoinedLobbyHandler;
                    loadModules();
                }

                GameState state = getGameState();

                handlePlayers(state);

                readChat();

                if (shouldReadLobby)
                {
                    var gameCode = ProcessMemory.ReadString(ProcessMemory.Read<IntPtr>(GameAssemblyPtr, GameOffsets.GameStartManagerOffset, 0x5c, 0, 0x20, 0x28));
                    string[] split;
                    if (gameCode != null && gameCode.Length > 0 && (split = gameCode.Split('\n')).Length == 2)
                    {
                        PlayRegion region = (PlayRegion)((4 - (ProcessMemory.Read<int>(GameAssemblyPtr, GameOffsets.ServerManagerOffset, 0x5c, 0, 0x10, 0x8, 0x8) & 0b11)) % 3);

                        this.latestLobbyEventArgs = new LobbyEventArgs()
                        {
                            LobbyCode = split[1],
                            Region = region
                        };
                        shouldReadLobby = false;
                        shouldTransmitLobby = true; // since this is probably new info
                    }
                }

                if (shouldTransmitLobby)
                {
                    if (this.latestLobbyEventArgs != null)
                    {
                        JoinedLobby?.Invoke(this, this.latestLobbyEventArgs);
                    }
                    shouldTransmitLobby = false;
                }

                Thread.Sleep(250);
            }
        }

        private void loadModules()
        {
            while (true)
            {
                foreach (var module in ProcessMemory.modules)
                    if (module.Name.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        GameAssemblyPtr = module.BaseAddress;
                        if (!VerifySteamHash(module.FileName))
                        {
                            Console.WriteLine("GameVerifier", $"Client verification: FAIL.");
                        }
                        else
                        {
                            Console.WriteLine("GameVerifier", $"Client verification: PASS.");
                        }

                        foundModule = true;
                        break;
                    }

                if (!foundModule)
                {
                    Console.WriteLine("Still looking for modules...");
                    Thread.Sleep(500); // delay and try again
                    ProcessMemory.LoadModules();
                }
                else
                {
                    break; // we have found all modules
                }
            }
        }

        private GameState handlePlayers(GameState state)
        {
            var allPlayersPtr = ProcessMemory.Read<IntPtr>(GameAssemblyPtr, GameOffsets.GameDataOffset, 0x5C, 0, 0x24);
            var allPlayers = ProcessMemory.Read<IntPtr>(allPlayersPtr, 0x08);
            var playerCount = ProcessMemory.Read<int>(allPlayersPtr, 0x0C);

            var playerAddrPtr = allPlayers + 0x10;

            // check if exile causes end
            if (oldState == GameState.DISCUSSION && state == GameState.TASKS)
            {
                var exiledPlayerId = ProcessMemory.ReadWithDefault<byte>(GameAssemblyPtr, 255, GameOffsets.MeetingHudOffset, 0x5C, 0, 0x94, 0x08);
                int impostorCount = 0, innocentCount = 0;

                for (var i = 0; i < playerCount; i++)
                {
                    var pi = ProcessMemory.Read<PlayerInfo>(playerAddrPtr, 0, 0);
                    playerAddrPtr += 4;

                    if (pi.PlayerId == exiledPlayerId)
                        PlayerChanged?.Invoke(this, new PlayerChangedEventArgs
                        {
                            Action = PlayerAction.Exiled,
                            Name = pi.GetPlayerName(),
                            IsDead = pi.GetIsDead(),
                            Disconnected = pi.GetIsDisconnected(),
                            Color = pi.GetPlayerColor(),
                            isImposter = pi.IsImpostor
                        });

                    // skip invalid, dead and exiled players
                    if (pi.PlayerName == 0 || pi.PlayerId == exiledPlayerId || pi.IsDead == 1 ||
                        pi.Disconnected == 1) continue;

                    if (pi.IsImpostor == 1)
                        impostorCount++;
                    else
                        innocentCount++;
                }

                if (impostorCount == 0 || impostorCount >= innocentCount)
                {
                    exileCausesEnd = true;
                    state = GameState.LOBBY;
                }
            }

            if (state != oldState || shouldForceTransmitState)
            {
                GameStateChanged?.Invoke(this, new GameStateChangedEventArgs { NewState = state });
                shouldForceTransmitState = false;
            }

            if (state != oldState && state == GameState.LOBBY)
            {
                shouldReadLobby = true; // will eventually transmit
            }

            oldState = state;

            newPlayerInfos.Clear();

            playerAddrPtr = allPlayers + 0x10;

            for (var i = 0; i < playerCount; i++)
            {
                var pi = ProcessMemory.Read<PlayerInfo>(playerAddrPtr, 0, 0);
                playerAddrPtr += 4;
                if (pi.PlayerName == 0) continue;
                var playerName = pi.GetPlayerName();
                if (playerName.Length == 0) continue;

                newPlayerInfos[playerName] = pi; // add to new playerinfos for comparison later

                if (!oldPlayerInfos.ContainsKey(playerName)) // player wasn't here before, they just joined
                {
                    PlayerChanged?.Invoke(this, new PlayerChangedEventArgs
                    {
                        Action = PlayerAction.Joined,
                        Name = playerName,
                        IsDead = pi.GetIsDead(),
                        Disconnected = pi.GetIsDisconnected(),
                        Color = pi.GetPlayerColor(),
                        isImposter = pi.IsImpostor
                    });
                }
                else
                {
                    // player was here before, we have an old playerInfo to compare against
                    var oldPlayerInfo = oldPlayerInfos[playerName];
                    if (!oldPlayerInfo.GetIsDead() && pi.GetIsDead()) // player just died
                        PlayerChanged?.Invoke(this, new PlayerChangedEventArgs
                        {
                            Action = PlayerAction.Died,
                            Name = playerName,
                            IsDead = pi.GetIsDead(),
                            Disconnected = pi.GetIsDisconnected(),
                            Color = pi.GetPlayerColor(),
                            isImposter = pi.IsImpostor
                        });

                    if (oldPlayerInfo.ColorId != pi.ColorId)
                        PlayerChanged?.Invoke(this, new PlayerChangedEventArgs
                        {
                            Action = PlayerAction.ChangedColor,
                            Name = playerName,
                            IsDead = pi.GetIsDead(),
                            Disconnected = pi.GetIsDisconnected(),
                            Color = pi.GetPlayerColor(),
                            isImposter = pi.IsImpostor
                        });

                    if (!oldPlayerInfo.GetIsDisconnected() && pi.GetIsDisconnected())
                        PlayerChanged?.Invoke(this, new PlayerChangedEventArgs
                        {
                            Action = PlayerAction.Disconnected,
                            Name = playerName,
                            IsDead = pi.GetIsDead(),
                            Disconnected = pi.GetIsDisconnected(),
                            Color = pi.GetPlayerColor(),
                            isImposter = pi.IsImpostor
                        });
                }
            }

            foreach (var kvp in oldPlayerInfos)
            {
                var pi = kvp.Value;
                var playerName = kvp.Key;
                if (!newPlayerInfos.ContainsKey(playerName)) // player was here before, isn't now, so they left
                    PlayerChanged?.Invoke(this, new PlayerChangedEventArgs
                    {
                        Action = PlayerAction.Left,
                        Name = playerName,
                        IsDead = pi.GetIsDead(),
                        Disconnected = pi.GetIsDisconnected(),
                        Color = pi.GetPlayerColor(),
                        isImposter = pi.IsImpostor
                    });
            }

            oldPlayerInfos.Clear();

            var emitAll = false;
            if (shouldForceUpdatePlayers)
            {
                shouldForceUpdatePlayers = false;
                emitAll = true;
            }

            foreach (var kvp in newPlayerInfos) // do this instead of assignment so they don't point to the same object
            {
                var pi = kvp.Value;
                oldPlayerInfos[kvp.Key] = pi;
                if (emitAll)
                    PlayerChanged?.Invoke(this, new PlayerChangedEventArgs
                    {
                        Action = PlayerAction.ForceUpdated,
                        Name = kvp.Key,
                        IsDead = pi.GetIsDead(),
                        Disconnected = pi.GetIsDisconnected(),
                        Color = pi.GetPlayerColor(),
                        isImposter = pi.IsImpostor
                    });
            }

            return state;
        }

        private GameState getGameState()
        {
            GameState state;
            var meetingHud = ProcessMemory.Read<IntPtr>(GameAssemblyPtr, GameOffsets.MeetingHudOffset, 0x5C, 0);
            var meetingHud_cachePtr = meetingHud == IntPtr.Zero ? 0 : ProcessMemory.Read<uint>(meetingHud, 0x8);
            var meetingHudState =
                meetingHud_cachePtr == 0
                    ? 4
                    : ProcessMemory.ReadWithDefault(meetingHud, 4, 0x84);
            var gameState = ProcessMemory.Read<int>(GameAssemblyPtr, GameOffsets.AmongUsClientOffset, 0x5C, 0, 0x64);

            switch (gameState)
            {
                case 0:
                    state = GameState.MENU;
                    exileCausesEnd = false;
                    break;
                case 1:
                case 3:
                    state = GameState.LOBBY;
                    exileCausesEnd = false;
                    break;
                default:
                    {
                        if (exileCausesEnd)
                            state = GameState.LOBBY;
                        else if (meetingHudState < 4)
                            state = GameState.DISCUSSION;
                        else
                            state = GameState.TASKS;

                        break;
                    }
            }

            return state;
        }

        private void readChat()
        {
            var chatBubblesPtr = ProcessMemory.Read<IntPtr>(GameAssemblyPtr, GameOffsets.HudManagerOffset, 0x5C, 0, 0x28, 0xC, 0x14);
            prevChatBubsVersion = ProcessMemory.Read<int>(GameAssemblyPtr, GameOffsets.HudManagerOffset, 0x5C, 0, 0x28, 0xC, 0x14, 0x10);
            var poolSize = 20; // = ProcessMemory.Read<int>(GameAssemblyPtr, 0xD0B25C, 0x5C, 0, 0x28, 0xC, 0xC)

            var numChatBubbles = ProcessMemory.Read<int>(chatBubblesPtr, 0xC);
            var chatBubsVersion = ProcessMemory.Read<int>(chatBubblesPtr, 0x10);
            var chatBubblesAddr = ProcessMemory.Read<IntPtr>(chatBubblesPtr, 0x8) + 0x10;
            var chatBubblePtrs = ProcessMemory.ReadArray(chatBubblesAddr, numChatBubbles);

            var newMsgs = 0;

            if (chatBubsVersion > prevChatBubsVersion) // new message has been sent
            {
                if (chatBubsVersion > poolSize) // increments are twofold (push to and pop from pool)
                {
                    if (prevChatBubsVersion > poolSize)
                        newMsgs = (chatBubsVersion - prevChatBubsVersion) >> 1;
                    else
                        newMsgs = poolSize - prevChatBubsVersion + ((chatBubsVersion - poolSize) >> 1);
                }
                else
                {
                    newMsgs = chatBubsVersion - prevChatBubsVersion;
                }
            }
            else if (chatBubsVersion < prevChatBubsVersion) // reset
            {
                if (chatBubsVersion > poolSize) // increments are twofold (push to and pop from pool)
                    newMsgs = poolSize + ((chatBubsVersion - poolSize) >> 1);
                else // single increments
                    newMsgs = chatBubsVersion;
            }

            prevChatBubsVersion = chatBubsVersion;

            for (var i = numChatBubbles - newMsgs; i < numChatBubbles; i++)
            {
                var msgText = ProcessMemory.ReadString(ProcessMemory.Read<IntPtr>(chatBubblePtrs[i], 0x20, 0x28));
                if (msgText.Length == 0) continue;
                var msgSender = ProcessMemory.ReadString(ProcessMemory.Read<IntPtr>(chatBubblePtrs[i], 0x1C, 0x28));
                var oldPlayerInfo = oldPlayerInfos[msgSender];
                ChatMessageAdded?.Invoke(this, new ChatMessageEventArgs
                {
                    Sender = msgSender,
                    Message = msgText,
                    Color = oldPlayerInfo.GetPlayerColor()
                });
            }
        }

        public static AmongUsReader getInstance()
        {
            return instance;
        }

        private void JoinedLobbyHandler(object sender, LobbyEventArgs e)
        {

            Console.WriteLine(JsonSerializer.Serialize(e));
        }

        private void PlayerChangedHandler(object sender, PlayerChangedEventArgs e)
        {
            Console.WriteLine(JsonSerializer.Serialize(e));

        }

        private void GameStateChangedHandler(object sender, GameStateChangedEventArgs e)
        {
            if (JsonSerializer.Serialize(e.NewState).Trim() == "2")
            {
                Console.WriteLine("Voting time");
            }
            else if (JsonSerializer.Serialize(e.NewState).Trim() == "1")
            {
                Console.WriteLine("In game");
            }

            Console.WriteLine("state: " + JsonSerializer.Serialize(e.NewState));
        }

        public static bool VerifySteamHash(string executablePath)
        {
            var baseDllFolder = Path.Combine(Directory.GetParent(executablePath).FullName, "\\Among Us_Data\\Plugins\\x86\\");
            var steam_apiCert = AuthenticodeTools.IsTrusted(Path.Combine(baseDllFolder, "steam_api.dll"));
            var steam_api64Cert = AuthenticodeTools.IsTrusted(Path.Combine(baseDllFolder, "steam_api64.dll"));
            return (steam_apiCert) && (steam_api64Cert);
        }

        public bool isConnected() => foundModule;

        public IEnumerable<KeyValuePair<string, PlayerInfo>> GetDeadPlayers()
        {
            return newPlayerInfos.Where(player => player.Value.GetIsDead());
        }

        public IEnumerable<KeyValuePair<string, PlayerInfo>> GetAlivePlayers()
        {
            return newPlayerInfos.Where(player => !player.Value.GetIsDead() && player.Value.Disconnected == 0);
        }
    }
}
