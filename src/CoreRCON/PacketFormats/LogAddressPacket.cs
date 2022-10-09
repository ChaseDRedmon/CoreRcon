using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CoreRCON.PacketFormats
{
    // Structure of a LogAddress packet from SRCDS:
    // 255 255 255 255
    // 82 or 83 (82 = no password, 83 = password)
    // if 82, the rest of the packet is the body.
    // Not sure what happens if it's 83, since I can't get my test server to return one even with sv_logsecret set.

    internal static partial class HLRegexParser
    {
        [GeneratedRegex(@"L (\d{2}/\d{2}/\d{4} - \d{2}:\d{2}:\d{2}):", RegexOptions.Compiled, 1000)]
        private static partial Regex HLLogStandard();
        
        /// <summary>
        /// Gets timestamp
        /// </summary>
        /// <returns>A bool whether the body matches the HL Log Standard Timestamp format</returns>
        /// <seealso href="https://developer.valvesoftware.com/wiki/HL_Log_Standard"/>
        public static Match IsValidTimestamp(string body) => HLLogStandard().Match(body);
    }

    public struct LogAddressPacket
    {
        /// <summary>
        /// The body of the packet with the timestamp removed.
        /// </summary>
        public readonly string Body;

        /// <summary>
        /// [UNSUPPORTED] If the packet was sent with sv_logsecret set.
        /// </summary>
        public readonly bool HasPassword;

        /// <summary>
        /// The raw body of the packet.
        /// </summary>
        public readonly string RawBody;

        /// <summary>
        /// The timestamp at which the packet was sent (not received).
        /// </summary>
        public readonly DateTime Timestamp;

        /// <summary>
        /// Create a new packet.
        /// </summary>
        /// <param name="hasPassword">[UNSUPPORTED] If the server returned this packet with sv_logsecret set.</param>
        /// <param name="rawBody">The raw body from the packet.</param>
        /// TODO This method should probably be removed and replace with a Parse method instead. This constructor needs to do less work
        public LogAddressPacket(bool hasPassword, string rawBody)
        {
            HasPassword = hasPassword;
            RawBody = rawBody;

            // Get timestamp
            // https://developer.valvesoftware.com/wiki/HL_Log_Standard
            //var match = new Regex(@"L (\d{2}/\d{2}/\d{4} - \d{2}:\d{2}:\d{2}):").Match(rawBody);
            var match = HLRegexParser.IsValidTimestamp(rawBody);
            if (match.Success)
            {
                var value = match.Groups[1].ValueSpan;
                Timestamp = DateTime.ParseExact(value, "MM/dd/yyyy - HH:mm:ss", CultureInfo.InvariantCulture);
            }
            else
            {
                Timestamp = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            }

            // Get body without the date/time
            Body = rawBody[25..];
        }

        public override string ToString() => RawBody;
        
        /// <summary>
        /// Converts a buffer to a packet.
        /// </summary>
        /// <param name="buffer">Buffer to read.</param>
        /// <returns>Created packet.</returns>
        internal static LogAddressPacket FromBytes(Span<byte> buffer)
        {
            if (buffer.Length < 7) throw new InvalidDataException("LogAddress packet is of an invalid length.");
            
            if (!buffer[..4].SequenceEqual(new ReadOnlySpan<byte>(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })))
                throw new InvalidDataException("LogAddress packet does not contain a valid header.");
            
            // 83 = magic byte
            bool hasPassword = buffer[5] == 83;

            try
            {
                var result = Span<char>.Empty;
                Encoding.UTF8.GetChars(buffer.Slice(5, buffer.Length - 7), result);
                var body = new string(result);
                
                // Force string to \r\n line endings
                body = Regex.Replace(body, @"\r\n|\n\r|\n|\r", "\r\n");
                return new LogAddressPacket(hasPassword, body);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{DateTime.Now} - Error reading logaddress packet from server: " + ex.Message);
                return new LogAddressPacket(hasPassword, string.Empty);
            }
        }
    }
}
