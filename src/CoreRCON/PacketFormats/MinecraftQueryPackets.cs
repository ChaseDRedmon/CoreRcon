using System;
using System.Collections.Generic;

namespace CoreRCON.PacketFormats
{
    public record MinecraftQueryInfo : IMinecraftQueryInfo
    {
        public string MessageOfTheDay { get; private init; }
        public string Gametype { get; private init; }
        public string GameId { get; private init; }
        public string Version { get; private init; }
        public string Plugins { get; private init; }
        public string Map { get; private init; }
        public string NumPlayers { get; private init; }
        public string MaxPlayers { get; private init; }
        public string HostPort { get; private init; }
        public string HostIp { get; private init; }
        public IEnumerable<string> Players { get; private init; }

        public static MinecraftQueryInfo FromBytes(ReadOnlySpan<byte> buffer)
        {
            int i = 16; // 1x type, 4x session, 11x padding
            var serverinfo = buffer.ReadNullTerminatedStringDictionary(i, ref i);
            
            i += 10;
            var players = buffer.ReadNullTerminatedStringArray(i, ref i);

            return new MinecraftQueryInfo
            {
                MessageOfTheDay = serverinfo["hostname"],
                Gametype = serverinfo["gametype"],
                GameId = serverinfo["game_id"],
                Version = serverinfo["version"],
                Plugins = serverinfo["plugins"],
                Map = serverinfo["map"],
                NumPlayers = serverinfo["numplayers"],
                MaxPlayers = serverinfo["maxplayers"],
                HostPort = serverinfo["hostport"],
                HostIp = serverinfo["hostip"],
                Players = players
            };
        }
    }
}
