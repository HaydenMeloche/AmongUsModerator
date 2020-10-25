using System;
using System.Runtime.InteropServices;

namespace SDK
{
    [StructLayout(LayoutKind.Explicit)]
    public struct PlayerInfo
    {
        [FieldOffset(8)] public byte PlayerId;
        [FieldOffset(12)] public uint PlayerName;
        [FieldOffset(16)] public byte ColorId;
        [FieldOffset(20)] public uint HatId;
        [FieldOffset(24)] public uint PetId;
        [FieldOffset(28)] public uint SkinId;
        [FieldOffset(32)] public byte Disconnected;
        [FieldOffset(36)] public IntPtr Tasks;
        [FieldOffset(40)] public byte IsImpostor;
        [FieldOffset(41)] public byte IsDead;
        [FieldOffset(44)] public IntPtr _object;

        public bool GetIsDead()
        {
            return IsDead > 0;
        }

        public string GetPlayerName()
        {
            return ProcessMemory.ReadString((IntPtr)PlayerName);
        }

        public PlayerColor GetPlayerColor()
        {
            return (PlayerColor)ColorId;
        }

        public bool GetIsDisconnected()
        {
            return Disconnected > 0;
        }

        public bool IsImposter()
        {
            return Convert.ToBoolean(IsImpostor);
        }
    }
}
