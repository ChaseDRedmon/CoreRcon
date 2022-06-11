using System;
using System.Collections.Generic;

namespace CoreRCON.PacketFormats
{
    public interface IQueryInfo<out T> where T : class
    {
        static abstract T FromBytes(ReadOnlySpan<byte> buffer);
    }
    
    public interface ISourceQueryInfo : IQueryInfo<SourceQueryInfo>
    {
        byte Bots { get; }
        ServerEnvironment Environment { get; }
        string Folder { get; }
        string Game { get; }
        short GameId { get; }
        string Map { get; }
        byte MaxPlayers { get; }
        string Name { get; }
        byte Players { get; }
        byte ProtocolVersion { get; }
        ServerType Type { get; }
        ServerVAC VAC { get; }
        ServerVisibility Visibility { get; }
        
        static new abstract SourceQueryInfo FromBytes(ReadOnlySpan<byte> buffer);
    }
    
    public interface IMinecraftQueryInfo : IQueryInfo<MinecraftQueryInfo>
    {
        string MessageOfTheDay { get; }
        string Gametype { get; }
        string GameId { get; }
        string Version { get; }
        string Plugins { get; }
        string Map { get; }
        string NumPlayers { get; }
        string MaxPlayers { get; }
        string HostPort { get; }
        string HostIp { get; }

        IEnumerable<string> Players { get; }
        
        static new abstract MinecraftQueryInfo FromBytes(ReadOnlySpan<byte> buffer);
    }
}
