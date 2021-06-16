using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Mirage.Serialization
{
    /// <summary>
    /// a class that holds writers for the different types
    /// Note that c# creates a different static variable for each
    /// type
    /// This will be populated by the weaver
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public static class Writer<T>
    {
        public static Action<NetworkWriter, T> Write { internal get; set; }
    }

    /// <summary>
    /// a class that holds readers for the different types
    /// Note that c# creates a different static variable for each
    /// type
    /// This will be populated by the weaver
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public static class Reader<T>
    {
        public static Func<NetworkReader, T> Read { internal get; set; }
    }

    public static class GenericTypesSerializationExtensions
    {
        /// <summary>
        /// Writes any type that mirror supports
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [WeaverIgnore]
        public static void Write<T>(this NetworkWriter writer, T value)
        {
            if (Writer<T>.Write == null)
                throw new KeyNotFoundException($"No writer found for {typeof(T)}. See https://miragenet.github.io/Mirage/Articles/General/Troubleshooting.html for details");

            Writer<T>.Write(writer, value);
        }

        /// <summary>
        /// Reads any data type that mirror supports
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [WeaverIgnore]
        public static T Read<T>(this NetworkReader reader)
        {
            if (Reader<T>.Read == null)
                throw new KeyNotFoundException($"No reader found for {typeof(T)}. See https://miragenet.github.io/Mirage/Articles/General/Troubleshooting.html for details");

            return Reader<T>.Read(reader);
        }
    }
}
