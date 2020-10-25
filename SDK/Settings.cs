using Config.Net;
using System;
using System.IO;


namespace SDK
{
    public static class Settings
    {
        public static ConsoleInterface output = new ConsoleInterface();
    }

    public static class GameOffsets
    {
        public static string GameHash { get; } = "74C7DF9C5C722CC641018880F29F2C4C8F52C0720DFC808FD0060D0E7552F192";

        public static int AmongUsClientOffset { get; } = 0x1468840;

        public static int GameDataOffset { get; } = 0x1468864;

        public static int MeetingHudOffset { get; } = 0x14686A0;

        public static int GameStartManagerOffset { get; } = 0x13FB424;

        public static int HudManagerOffset { get; } = 0x13EEB44;

        public static int ServerManagerOffset { get; } = 0x13F14E4;
    }
}
