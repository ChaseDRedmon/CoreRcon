using System;
using System.Buffers.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CoreRCON.PacketFormats;
using OneOf;

namespace CoreRCON
{
    public interface IQuery : IDisposable
    {
        Task<OneOf<SourceQueryInfo?, MinecraftQueryInfo?>> QueryInfoAsync<T>(T queryType, IPAddress address, ushort port, ServerType type, CancellationToken ct = default) where T : class, IMinecraftQueryInfo, ISourceQueryInfo;
        Task<OneOf<SourceQueryInfo?, MinecraftQueryInfo?>> QueryInfoAsync<T>(T queryType, IPEndPoint endpoint, ServerType type, CancellationToken ct = default) where T : class, IMinecraftQueryInfo, ISourceQueryInfo;
        Task<MinecraftQueryInfo?> QueryMinecraftInfoAsync(IPEndPoint endpoint, CancellationToken ct = default);
        Task<SourceQueryInfo?> QuerySourceInfoAsync(IPEndPoint endpoint, CancellationToken ct = default);
        Task<ServerQueryPlayer?[]> QueryPlayersAsync(IPAddress address, ushort port, CancellationToken ct = default);
        Task<ServerQueryPlayer?[]> QueryPlayersAsync(IPEndPoint host, CancellationToken ct = default);
    }

    public class Query : IQuery
    {
        private static readonly byte[] _magic = { 0xFE, 0xFD }; // Minecraft 'magic' bytes.
        private static readonly byte[] _sessionid = { 0x01, 0x02, 0x03, 0x04 };
        private static readonly byte[] _asInfochallengeResponse = { 0xFF, 0xFF, 0xFF, 0xFF, 0x41 };
        private static readonly byte[] _minecraftPadding = { 0x00, 0x00, 0x00, 0x00 };
        
        private static readonly Memory<byte> _handShake = new byte[] { _magic[0], _magic[1], (byte)PacketType.Handshake, _sessionid[0], _sessionid[1], _sessionid[2], _sessionid[3] };
        private static readonly Memory<byte> _asInfoPayload = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x54, 0x53, 0x6F, 0x75, 0x72, 0x63, 0x65, 0x20, 0x45, 0x6E, 0x67, 0x69, 0x6E, 0x65, 0x20, 0x51, 0x75, 0x65, 0x72, 0x79, 0x00 };
        private static readonly Memory<byte> _sourceChallenge = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x55, 0xFF, 0xFF, 0xFF, 0xFF };

        private readonly UdpClient _udpClient;
        
        private enum PacketType
        {
            Handshake = 0x09,
            Stat = 0x00
        }
        
        public Query()
        {
            _udpClient = new UdpClient();
        }
        
        public async Task<OneOf<SourceQueryInfo?, MinecraftQueryInfo?>> QueryInfoAsync<T>(T queryType, IPAddress address, ushort port, ServerType type, CancellationToken ct = default) where T : class, IMinecraftQueryInfo, ISourceQueryInfo
        {
            var endpoint = new IPEndPoint(address, port);
            return await QueryInfoAsync(queryType, endpoint, type, ct);
        }
        
        public async Task<OneOf<SourceQueryInfo?, MinecraftQueryInfo?>> QueryInfoAsync<T>(T queryType, IPEndPoint endpoint, ServerType type, CancellationToken ct = default) where T : class, IMinecraftQueryInfo, ISourceQueryInfo
        {
            if (queryType is IMinecraftQueryInfo minecraftQueryInfo)
            {
                return await QueryMinecraftInfoAsync(endpoint, ct);
            }

            return await QuerySourceInfoAsync(endpoint, ct);
        }

        public async Task<MinecraftQueryInfo?> QueryMinecraftInfoAsync(IPEndPoint endpoint, CancellationToken ct = default)
        {
            // Requirement
            var challenge = await SendChallengeAsync(endpoint, ServerType.Minecraft, ct);
            
            // TODO Perhaps return a Result object saying we failed?
            if (challenge.Length == 0)
                return null;
            
            var datagram = new byte[11 + challenge.Length];
            
            // Magic minecraft bytes
            datagram[0] = 0xFE;
            datagram[1] = 0xFD;
            datagram[2] = (byte)PacketType.Stat;
            
            // Session Id Bytes
            datagram[3] = 0x01;
            datagram[4] = 0x02;
            datagram[5] = 0x03;
            datagram[6] = 0x04;
            
            Buffer.BlockCopy(challenge, 0, datagram, 7, datagram.Length);

            // Minecraft Padding bytes
            datagram[^4] = 0x00;
            datagram[^3] = 0x00;
            datagram[^2] = 0x00;
            datagram[^1] = 0x00;
            
            await _udpClient.SendAsync(datagram, endpoint, ct);
            var mcResponce = await _udpClient.ReceiveAsync(ct);
            return MinecraftQueryInfo.FromBytes(mcResponce.Buffer);
        }
        
        public async Task<SourceQueryInfo?> QuerySourceInfoAsync(IPEndPoint endpoint, CancellationToken ct = default)
        {
            await _udpClient.SendAsync(_asInfoPayload, endpoint, ct);
            var response = await _udpClient.ReceiveAsync(ct);
            var payload = response.Buffer;
            
            var challengeEquality = BuildChallengeResponse(payload, out var challenge);
            
            byte[]? challengeResponse = null;
            if (challengeEquality)
            {
                await _udpClient.SendAsync(challenge, challenge.Length, endpoint);
                var udpResult = await _udpClient.ReceiveAsync(ct);
                challengeResponse = udpResult.Buffer;
            }

            var sourceInfo = challengeEquality ? payload : challengeResponse;
            return SourceQueryInfo.FromBytes(sourceInfo);
            
            // Try to build a challenge response with minimal heap allocation.
            static bool BuildChallengeResponse(ReadOnlySpan<byte> source, out byte[] challengeResponse)
            {
                var challengeSlice = source[.._asInfochallengeResponse.Length];
                var challengeEquality = challengeSlice.SequenceEqual(_asInfochallengeResponse);

                var challengeConcat = source.Slice(5, 4);
                    
                Span<byte> challenge = stackalloc byte[_asInfoPayload.Length + 4];
                _asInfoPayload.Span.CopyTo(challenge);
                    
                challenge[^4] = challengeConcat[0];
                challenge[^3] = challengeConcat[1];
                challenge[^2] = challengeConcat[2];
                challenge[^1] = challengeConcat[3];
                    
                challengeResponse = challenge.ToArray();
                return challengeEquality;
            }
        }

        public async Task<ServerQueryPlayer?[]> QueryPlayersAsync(IPAddress address, ushort port, CancellationToken ct = default) =>
            await QueryPlayersAsync(new IPEndPoint(address, port), ct);
        public async Task<ServerQueryPlayer?[]> QueryPlayersAsync(IPEndPoint host, CancellationToken ct = default)
        {
            var sourceChallenge = await SendChallengeAsync(host, ServerType.Source, ct);

            if (sourceChallenge.Length == 0)
                return Array.Empty<ServerQueryPlayer>();
            
            var responseChallenge = new byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0x55, 
                sourceChallenge[0], 
                sourceChallenge[1], 
                sourceChallenge[2],
                sourceChallenge[3]
            };

            var sendTask = _udpClient.SendAsync(responseChallenge, host, ct);
            await sendTask;
            
            var responseTask = _udpClient.ReceiveAsync(ct);
            var udpReceiveResult = await responseTask;

            if (sendTask.IsCanceled || responseTask.IsCanceled)
                return Array.Empty<ServerQueryPlayer>();

            var buffer = udpReceiveResult.Buffer;
            return ServerQueryPlayer.FromBytes(buffer);
        }

        public void Dispose()
        {
            _udpClient.Dispose();
        }
        
        private async Task<byte[]> SendChallengeAsync(IPEndPoint host, ServerType serverType, CancellationToken ct = default)
        {
            return serverType switch
            {
                ServerType.Source => await ChallengeSource(),
                ServerType.Minecraft => await ChallengeMinecraft(),
                _ => throw new ArgumentOutOfRangeException(nameof(serverType), serverType, null)
            };
            
            async Task<byte[]> ChallengeSource()
            {
                var sourceChallenge = await ExecuteChallenge(_sourceChallenge);
                
                // Exit method if array is empty
                if (sourceChallenge.Length == 0)
                    return sourceChallenge;
                
                return sourceChallenge.AsSpan().Slice(5, 4).ToArray();
            }

            async Task<byte[]> ChallengeMinecraft()
            {
                var minecraftChallenge = await ExecuteChallenge(_handShake);
                
                // Exit method if array is empty
                if (minecraftChallenge.Length == 0)
                    return minecraftChallenge;
                
                return BuildMinecraftChallengeResponse(minecraftChallenge);
            }

            async ValueTask<byte[]> ExecuteChallenge(Memory<byte> dataToSend)
            {
                var queryTask = _udpClient.SendAsync(dataToSend, host, ct);
                await queryTask;

                var responseTask = _udpClient.ReceiveAsync(ct);
                var udpReceiveResult = await responseTask;
                var buffer = udpReceiveResult.Buffer;
                
                if (queryTask.IsCanceled || responseTask.IsCanceled)
                    return Array.Empty<byte>();

                return buffer;
            }
        }
        
        private byte[] BuildMinecraftChallengeResponse(Span<byte> buffer)
        {
            ReadOnlySpan<byte> challenge = buffer.Slice(5, buffer.Length);
            _ = Utf8Parser.TryParse(challenge, out int challengeInt, out _);
            var reversedChallengeInt = BitConverter.GetBytes(challengeInt).AsSpan();
            reversedChallengeInt.Reverse();
            
            return reversedChallengeInt.ToArray();
        }
    }
}
