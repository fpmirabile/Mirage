using System;
using UnityEngine;

namespace Mirage
{
    /// <summary>
    /// SyncVars are used to synchronize a variable from the server to all clients automatically.
    /// <para>Value must be changed on server, not directly by clients.  Hook parameter allows you to define a client-side method to be invoked when the client gets an update from the server.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class SyncVarAttribute : PropertyAttribute
    {
        ///<summary>A function that should be called on the client when the value changes.</summary>
        public string hook;
    }

    /// <summary>
    /// Call this from a client to run this function on the server.
    /// <para>Make sure to validate input etc. It's not possible to call this from a server.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ServerRpcAttribute : Attribute
    {
        public int channel = Channel.Reliable;
        public bool requireAuthority = true;
    }

    public enum Client { Owner, Observers, Player }

    /// <summary>
    /// The server uses a Remote Procedure Call (RPC) to run this function on specific clients.
    /// <para>Note that if you set the target as Connection, you need to pass a specific connection as a parameter of your method</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ClientRpcAttribute : Attribute
    {
        public int channel = Channel.Reliable;
        public Client target = Client.Observers;
        public bool excludeOwner;
    }

    /// <summary>
    /// Prevents a method from running if server is not active.
    /// <para>Can only be used inside a NetworkBehaviour</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ServerAttribute : Attribute
    {
        /// <summary>
        /// If true,  when the method is called from a client, it throws an error
        /// If false, no error is thrown, but the method won't execute
        /// useful for unity built in methods such as Await, Update, Start, etc.
        /// </summary>
        public bool error = true;
    }

    /// <summary>
    /// Tell the weaver to generate  reader and writer for a class
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class NetworkMessageAttribute : Attribute
    {
    }

    /// <summary>
    /// Prevents this method from running if client is not active.
    /// <para>Can only be used inside a NetworkBehaviour</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ClientAttribute : Attribute
    {
        /// <summary>
        /// If true,  when the method is called from a client, it throws an error
        /// If false, no error is thrown, but the method won't execute
        /// useful for unity built in methods such as Await, Update, Start, etc.
        /// </summary>
        public bool error = true;
    }

    /// <summary>
    /// Prevents players without authority from running this method.
    /// <para>Can only be used inside a NetworkBehaviour</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class HasAuthorityAttribute : Attribute
    {
        /// <summary>
        /// If true,  when the method is called from a client, it throws an error
        /// If false, no error is thrown, but the method won't execute
        /// useful for unity built in methods such as Await, Update, Start, etc.
        /// </summary>
        public bool error = true;
    }

    /// <summary>
    /// Prevents nonlocal players from running this method.
    /// <para>Can only be used inside a NetworkBehaviour</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class LocalPlayerAttribute : Attribute
    {
        /// <summary>
        /// If true,  when the method is called from a client, it throws an error
        /// If false, no error is thrown, but the method won't execute
        /// useful for unity built in methods such as Await, Update, Start, etc.
        /// </summary>
        public bool error = true;
    }

    /// <summary>
    /// Converts a string property into a Scene property in the inspector
    /// </summary>
    public sealed class SceneAttribute : PropertyAttribute { }

    /// <summary>
    /// Used to show private SyncList in the inspector,
    /// <para> Use instead of SerializeField for non Serializable types </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ShowInInspectorAttribute : Attribute { }

    /// <summary>
    /// Draws UnityEvent as a foldout
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FoldoutEventAttribute : PropertyAttribute { }

    /// <summary>
    /// Tells Weaver to ignore an Extension method
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class WeaverIgnoreAttribute : PropertyAttribute { }

    /// <summary>
    /// Tells weaver how many bits to sue for field
    /// <para>Only works with interager fields (byte, int, ulong, etc)</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
    public class BitCountAttribute : Attribute
    {
        public int BitCount { get; set; }

        public BitCountAttribute(int bitCount)
        {
            BitCount = bitCount;
        }
    }

    /// <summary>
    /// Tells weaver how many bits to sue for field
    /// <para>Only works with interager fields (byte, int, ulong, etc)</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
    public class PackFloatAttribute : Attribute
    {
        /// <summary>
        /// How many bits
        /// </summary>
        public int BitCount { get; set; }
        /// <summary>
        /// Smallest value
        /// <para><b>Example:</b> Resolution of 0.1 means values will be rounded to that: 1.53 will be sent at 1.5</para>
        /// <para>Values will be rounded to nearest value, so 1.58 will around up to 1.6</para>
        /// </summary>
        public float Resolution { get; set; }

        public bool Signed { get; set; }

        public PackFloatAttribute(int bitCount, float resolution, bool signed)
        {
            BitCount = bitCount;
            Resolution = resolution;
            Signed = signed;
        }
    }
}
