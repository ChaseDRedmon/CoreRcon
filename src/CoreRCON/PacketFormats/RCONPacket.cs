using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;


namespace CoreRCON.PacketFormats
{
    /// <summary>
    /// Encapsulate RCONPacket specification.
    ///
    /// Detailed specification of RCON packets can be found here:
    /// https://developer.valvesoftware.com/wiki/Source_RCON_Protocol
    /// </summary>
    public record RCONPacket
    {
        /// <summary>
        /// The actual information held within.
        /// </summary>
        public required string Body { get; init; }
        
        /// <summary>
        /// Some kind of identifier to keep track of responses from the server.
        /// </summary>
        public required int Id { get; init; }
        
        /// <summary>
        /// What the server is supposed to do with the body of this packet.
        /// </summary>
        public required PacketType Type { get; init; }

        public override string ToString() => Body;
        
        /// <summary>
        /// Deconstructs the RCONPacket
        /// </summary>
        /// <param name="body"></param>
        /// <param name="id"></param>
        /// <param name="type"></param>
        public void Deconstruct(out string body, out int id, out PacketType type) => (body, id, type) = (Body, Id, Type);
        
        /// <summary>
        /// Converts a buffer to a packet.
        /// </summary>
        /// <param name="buffer">Buffer to read.</param>
        /// <exception cref="NullReferenceException">If buffer is null</exception>
        /// <exception cref="InvalidDataException">If buffer does not contain a sizeof field within the 4 four bytes</exception>
        /// <exception cref="InvalidDataException">If the length of the buffer is longer than <see cref="Constants.MAX_PACKET_SIZE"/></exception>
        /// <exception cref="InvalidDataException">If the packet size is larger than the buffer</exception>
        /// <exception cref="InvalidDataException">If the size is less than 10, packet is invalid</exception>
        /// <returns>Created packet.</returns>
        internal static RCONPacket FromBytes(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < 4) throw new InvalidDataException("Buffer does not contain a size field.");
            if (buffer.Length > Constants.MAX_PACKET_SIZE) throw new InvalidDataException("Buffer is too large for an RCON packet.");

            int size = BitConverter.ToInt32(buffer);
            if (size > buffer.Length - 4) throw new InvalidDataException("Packet size specified was larger then buffer");
            if (size < 10) throw new InvalidDataException("Packet received was invalid.");

            int id = BitConverter.ToInt32(buffer[4..]);
            PacketType type = (PacketType)BitConverter.ToInt32(buffer[8..]);

            try
            {
                // Some games support UTF8 payloads, ASCII will also work due to backwards compatiblity
                var rawBody = Span<char>.Empty;
                Encoding.UTF8.GetChars(buffer.Slice(12, size - 10), rawBody);
                
                //char[] rawBody = Encoding.UTF8.GetChars(buffer, 12, size - 10);
                string body = new string(rawBody).TrimEnd();
                
                // Force Line endings to match environment
                body = Regex.Replace(body, @"\r\n|\n\r|\n|\r", "\r\n");
                return new RCONPacket { Body = body, Id = id, Type = type };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{DateTime.Now} - Error reading RCON packet body exception was: {ex.Message}");
                return new RCONPacket { Id = id, Type = type, Body = string.Empty };
            }
        }

        /// <summary>
        /// Serializes a packet to a byte array for transporting over a network.  Body is serialized as UTF8.
        /// </summary>
        /// <returns>Byte array with each field.</returns>
        internal byte[] ToBytes()
        {
            //Should also be compatible with ASCII only servers
            byte[] body = Encoding.UTF8.GetBytes(Body + "\0");
            int bodyLength = body.Length;

            using (var packet = new MemoryStream(12 + bodyLength))
            {
                packet.Write(BitConverter.GetBytes(9 + bodyLength), 0, 4);
                packet.Write(BitConverter.GetBytes(Id), 0, 4);
                packet.Write(BitConverter.GetBytes((int)Type), 0, 4);
                packet.Write(body, 0, bodyLength);
                packet.Write(new byte[] { 0 }, 0, 1);

                return packet.ToArray();
            }
        }
    }
}
