using System;
using System.Text;
using MessagePack;
using Newtonsoft.Json;

namespace XStateNet.Distributed.Core
{
    /// <summary>
    /// Handles message serialization for transport
    /// </summary>
    public static class MessageSerializer
    {
        public enum SerializationType
        {
            Json,
            MessagePack
        }

        private static SerializationType _defaultType = SerializationType.MessagePack;

        public static void SetDefaultSerializationType(SerializationType type)
        {
            _defaultType = type;
        }

        /// <summary>
        /// Serialize an object to bytes
        /// </summary>
        public static byte[] Serialize<T>(T obj, SerializationType? type = null)
        {
            type ??= _defaultType;

            if (obj == null)
                return Array.Empty<byte>();

            return type switch
            {
                SerializationType.Json => SerializeJson(obj),
                SerializationType.MessagePack => SerializeMessagePack(obj),
                _ => throw new NotSupportedException($"Serialization type {type} not supported")
            };
        }

        /// <summary>
        /// Deserialize bytes to object
        /// </summary>
        public static T? Deserialize<T>(byte[]? data, SerializationType? type = null)
        {
            if (data == null || data.Length == 0)
                return default;

            // If type is not specified, try to auto-detect
            if (type == null)
            {
                // Check if data looks like JSON (starts with { or [ or ")
                if (data.Length > 0 && (data[0] == '{' || data[0] == '[' || data[0] == '"'))
                {
                    type = SerializationType.Json;
                }
                else
                {
                    type = _defaultType;
                }
            }

            try
            {
                return type switch
                {
                    SerializationType.Json => DeserializeJson<T>(data),
                    SerializationType.MessagePack => DeserializeMessagePack<T>(data),
                    _ => throw new NotSupportedException($"Serialization type {type} not supported")
                };
            }
            catch (Exception ex) when (type == _defaultType && ex is not NotSupportedException)
            {
                // If default deserialization fails, try the other format
                var fallbackType = _defaultType == SerializationType.MessagePack
                    ? SerializationType.Json
                    : SerializationType.MessagePack;

                try
                {
                    return fallbackType switch
                    {
                        SerializationType.Json => DeserializeJson<T>(data),
                        SerializationType.MessagePack => DeserializeMessagePack<T>(data),
                        _ => default
                    };
                }
                catch
                {
                    // If fallback also fails, throw the original exception
                    throw ex;
                }
            }
        }

        /// <summary>
        /// Serialize a StateMachineMessage
        /// </summary>
        public static byte[] SerializeMessage(StateMachineMessage message)
        {
            return SerializeMessagePack(message);
        }

        /// <summary>
        /// Deserialize a StateMachineMessage
        /// </summary>
        public static StateMachineMessage? DeserializeMessage(byte[] data)
        {
            return DeserializeMessagePack<StateMachineMessage>(data);
        }

        private static byte[] SerializeJson<T>(T obj)
        {
            var json = JsonConvert.SerializeObject(obj);
            return Encoding.UTF8.GetBytes(json);
        }

        private static T? DeserializeJson<T>(byte[] data)
        {
            var json = Encoding.UTF8.GetString(data);
            return JsonConvert.DeserializeObject<T>(json);
        }

        private static byte[] SerializeMessagePack<T>(T obj)
        {
            return MessagePackSerializer.Serialize(obj);
        }

        private static T? DeserializeMessagePack<T>(byte[] data)
        {
            return MessagePackSerializer.Deserialize<T>(data);
        }

        /// <summary>
        /// Get type name for serialization
        /// </summary>
        public static string GetTypeName(Type type)
        {
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }

        /// <summary>
        /// Get type from type name
        /// </summary>
        public static Type? GetType(string typeName)
        {
            return Type.GetType(typeName);
        }
    }
}