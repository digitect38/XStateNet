using System;
using System.Runtime.InteropServices;
using System.Text;

namespace XStateNet.SharedMemory.Core
{
    /// <summary>
    /// Message envelope structure for shared memory transport
    /// Fixed header + variable-length payload
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MessageEnvelope
    {
        // Total message length including this header
        public int TotalLength;

        // Event name length
        public int EventNameLength;

        // Machine ID length
        public int MachineIdLength;

        // Payload length
        public int PayloadLength;

        // Timestamp in ticks
        public long Timestamp;

        // CRC32 checksum for data integrity
        public uint Checksum;

        // Reserved for future use
        public int Reserved;

        public const int HeaderSize = 32; // sizeof(MessageEnvelope)
    }

    /// <summary>
    /// Message builder and parser for shared memory transport
    /// </summary>
    public static class MessageEnvelopeHelper
    {
        /// <summary>
        /// Builds a complete message with envelope
        /// </summary>
        public static byte[] BuildMessage(string machineId, string eventName, byte[]? payload)
        {
            var eventNameBytes = Encoding.UTF8.GetBytes(eventName);
            var machineIdBytes = Encoding.UTF8.GetBytes(machineId);
            var payloadBytes = payload ?? Array.Empty<byte>();

            var envelope = new MessageEnvelope
            {
                EventNameLength = eventNameBytes.Length,
                MachineIdLength = machineIdBytes.Length,
                PayloadLength = payloadBytes.Length,
                Timestamp = DateTime.UtcNow.Ticks,
                Checksum = 0, // Calculate after building
                Reserved = 0
            };

            // Calculate total length
            envelope.TotalLength = MessageEnvelope.HeaderSize +
                                  eventNameBytes.Length +
                                  machineIdBytes.Length +
                                  payloadBytes.Length;

            // Allocate buffer
            var buffer = new byte[envelope.TotalLength];
            int offset = 0;

            // Write envelope header
            WriteEnvelope(buffer, ref offset, envelope);

            // Write event name
            Array.Copy(eventNameBytes, 0, buffer, offset, eventNameBytes.Length);
            offset += eventNameBytes.Length;

            // Write machine ID
            Array.Copy(machineIdBytes, 0, buffer, offset, machineIdBytes.Length);
            offset += machineIdBytes.Length;

            // Write payload
            if (payloadBytes.Length > 0)
            {
                Array.Copy(payloadBytes, 0, buffer, offset, payloadBytes.Length);
            }

            // Calculate and update checksum
            uint checksum = CalculateChecksum(buffer, MessageEnvelope.HeaderSize, buffer.Length - MessageEnvelope.HeaderSize);
            BitConverter.GetBytes(checksum).CopyTo(buffer, 20); // Offset of Checksum field

            return buffer;
        }

        /// <summary>
        /// Parses a message from buffer
        /// </summary>
        public static (string machineId, string eventName, byte[]? payload, long timestamp) ParseMessage(byte[] buffer)
        {
            if (buffer.Length < MessageEnvelope.HeaderSize)
            {
                throw new ArgumentException($"Buffer too small. Expected at least {MessageEnvelope.HeaderSize} bytes, got {buffer.Length}");
            }

            int offset = 0;
            var envelope = ReadEnvelope(buffer, ref offset);

            // Validate checksum
            uint calculatedChecksum = CalculateChecksum(buffer, MessageEnvelope.HeaderSize, buffer.Length - MessageEnvelope.HeaderSize);
            if (calculatedChecksum != envelope.Checksum)
            {
                throw new InvalidOperationException($"Checksum mismatch. Expected 0x{envelope.Checksum:X}, got 0x{calculatedChecksum:X}");
            }

            // Read event name
            var eventName = Encoding.UTF8.GetString(buffer, offset, envelope.EventNameLength);
            offset += envelope.EventNameLength;

            // Read machine ID
            var machineId = Encoding.UTF8.GetString(buffer, offset, envelope.MachineIdLength);
            offset += envelope.MachineIdLength;

            // Read payload
            byte[]? payload = null;
            if (envelope.PayloadLength > 0)
            {
                payload = new byte[envelope.PayloadLength];
                Array.Copy(buffer, offset, payload, 0, envelope.PayloadLength);
            }

            return (machineId, eventName, payload, envelope.Timestamp);
        }

        /// <summary>
        /// Reads just the envelope header to determine message size
        /// </summary>
        public static MessageEnvelope ReadEnvelopeHeader(byte[] buffer, int offset = 0)
        {
            return ReadEnvelope(buffer, ref offset);
        }

        private static void WriteEnvelope(byte[] buffer, ref int offset, MessageEnvelope envelope)
        {
            BitConverter.GetBytes(envelope.TotalLength).CopyTo(buffer, offset);
            offset += 4;

            BitConverter.GetBytes(envelope.EventNameLength).CopyTo(buffer, offset);
            offset += 4;

            BitConverter.GetBytes(envelope.MachineIdLength).CopyTo(buffer, offset);
            offset += 4;

            BitConverter.GetBytes(envelope.PayloadLength).CopyTo(buffer, offset);
            offset += 4;

            BitConverter.GetBytes(envelope.Timestamp).CopyTo(buffer, offset);
            offset += 8;

            BitConverter.GetBytes(envelope.Checksum).CopyTo(buffer, offset);
            offset += 4;

            BitConverter.GetBytes(envelope.Reserved).CopyTo(buffer, offset);
            offset += 4;
        }

        private static MessageEnvelope ReadEnvelope(byte[] buffer, ref int offset)
        {
            var envelope = new MessageEnvelope
            {
                TotalLength = BitConverter.ToInt32(buffer, offset),
                EventNameLength = BitConverter.ToInt32(buffer, offset + 4),
                MachineIdLength = BitConverter.ToInt32(buffer, offset + 8),
                PayloadLength = BitConverter.ToInt32(buffer, offset + 12),
                Timestamp = BitConverter.ToInt64(buffer, offset + 16),
                Checksum = BitConverter.ToUInt32(buffer, offset + 24),
                Reserved = BitConverter.ToInt32(buffer, offset + 28)
            };

            offset += MessageEnvelope.HeaderSize;
            return envelope;
        }

        /// <summary>
        /// Simple CRC32 checksum calculation
        /// </summary>
        private static uint CalculateChecksum(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;

            for (int i = offset; i < offset + length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
            }

            return ~crc;
        }
    }
}
