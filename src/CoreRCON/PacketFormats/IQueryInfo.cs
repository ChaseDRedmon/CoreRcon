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
        static new abstract SourceQueryInfo FromBytes(ReadOnlySpan<byte> buffer);
    }
    
    public interface IMinecraftQueryInfo : IQueryInfo<MinecraftQueryInfo>
    {
        static new abstract MinecraftQueryInfo FromBytes(ReadOnlySpan<byte> buffer);
    }
}
