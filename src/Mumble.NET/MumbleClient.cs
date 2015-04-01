﻿//-----------------------------------------------------------------------
// <copyright file="MumbleClient.cs" company="Matt Perry">
//     Copyright (c) Matt Perry. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Mumble
{
    using System;
    using System.Globalization;
    using System.Reflection;
    using System.Threading.Tasks;
    using Google.ProtocolBuffers;
    using Messages;

    /// <summary>
    /// Class representing a mumble client. Main entry point to the library.
    /// </summary>
    public sealed partial class MumbleClient : IDisposable
    {
        /// <summary>
        /// Constant for the default mumble port
        /// </summary>
        public const int DefaultPort = 64738;

        /// <summary>
        /// Client's Mumble version
        /// </summary>
        public static readonly System.Version ClientMumbleVersion = new System.Version(1, 2, 8);

        /// <summary>
        /// Network connection to the server
        /// </summary>
        private readonly IConnection connection;

        /// <summary>
        /// Initializes a new instance of the <see cref="MumbleClient"/> class.
        /// </summary>
        /// <param name="host">Hostname of Mumble server</param>
        public MumbleClient(string host)
            : this(host, DefaultPort)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MumbleClient"/> class.
        /// </summary>
        /// <param name="host">Hostname of Mumble server</param>
        /// <param name="port">Port on which the server is listening</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", 
            Justification = "Connection will be disposed")]
        public MumbleClient(string host, int port)
            : this(new Connection(host, port))
        {
            this.ServerInfo = new ServerInfo { HostName = host, Port = port };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MumbleClient"/> class.
        /// </summary>
        /// <param name="connection">Network connection to the server</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", 
            Justification = "There are a lot of protobuf message classes")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", 
            Justification = "Code generation and event handling make the event system a bit complex")]
        internal MumbleClient(IConnection connection)
        {
            this.connection = connection;
            this.SetupEvents();
        }

        /// <summary>
        /// Gets a value indicating whether we are connected
        /// </summary>
        public bool Connected { get; private set; }

        /// <summary>
        /// Gets basic Mumble server information
        /// </summary>
        public ServerInfo ServerInfo { get; private set; }

        /// <summary>
        /// Establish a connection with the Mumble server
        /// </summary>
        /// <param name="userName">Username of Mumble client</param>
        /// <param name="password">Password for authenticating with server</param>
        /// <returns>Empty task</returns>
        public async Task ConnectAsync(string userName, string password)
        {
            await this.connection.ConnectAsync();

            await this.SendMessage<Messages.Version.Builder>((builder) =>
            {
                builder.Version_ = EncodeVersion(ClientMumbleVersion);
                builder.Release = string.Format(CultureInfo.InvariantCulture, "Mumble.NET {0}", Assembly.GetExecutingAssembly().GetName().Version);
                builder.Os = Environment.OSVersion.Platform.ToString();
                builder.OsVersion = Environment.OSVersion.VersionString;
            });

            await this.SendMessage<Authenticate.Builder>((builder) =>
            {
                builder.Username = userName;
                builder.Password = password;
                builder.Opus = true;
            });

            await Task.Run(async () =>
            {
                while (!this.Connected)
                {
                    this.OnMessageReceived(await this.connection.ReadMessage());
                }
            });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.connection.Dispose();
        }

        /// <summary>
        /// Encode a version into mumble wire protocol version
        /// </summary>
        /// <param name="version">Version to encode</param>
        /// <returns>Unsigned integer representing the version</returns>
        private static uint EncodeVersion(System.Version version)
        {
            return (uint)((version.Major << 16) | (version.Minor << 8) | (version.Build & 0xFF));
        }

        /// <summary>
        /// Decode a version number
        /// </summary>
        /// <param name="version">Encoding version</param>
        /// <returns>Decoded version object</returns>
        private static System.Version DecodeVersion(uint version)
        {
            return new System.Version(
                (int)(version >> 16) & 0xFF,
                (int)(version >> 8) & 0xFF,
                (int)version & 0xFF);
        }

        /// <summary>
        /// Wire up the self subscribing events for updating basic state
        /// </summary>
        private void SetupEvents()
        {
            this.VersionReceived += this.HandleVersionReceived;
            this.CodecVersionReceived += this.HandleCodecVersionReceived;
            this.ServerSyncReceived += this.HandleServerSyncReceived;
        }

        /// <summary>
        /// Handle a Server Sync message
        /// </summary>
        /// <param name="sender">Object which sent the event</param>
        /// <param name="e">Event argument containing message</param>
        private void HandleServerSyncReceived(object sender, MessageReceivedEventArgs<ServerSync> e)
        {
            this.Connected = true;
        }

        /// <summary>
        /// Handle the codec negotiation
        /// </summary>
        /// <param name="sender">Object which sent the event</param>
        /// <param name="e">Event argument containing message</param>
        private void HandleCodecVersionReceived(object sender, MessageReceivedEventArgs<CodecVersion> e)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Handle a Version message
        /// </summary>
        /// <param name="sender">Object which sent the event</param>
        /// <param name="e">Event argument containing message</param>
        private void HandleVersionReceived(object sender, MessageReceivedEventArgs<Messages.Version> e)
        {
            this.ServerInfo.OS = e.Message.Os;
            this.ServerInfo.OSVersion = e.Message.OsVersion;
            this.ServerInfo.Release = e.Message.Release;
            this.ServerInfo.Version = DecodeVersion(e.Message.Version_);
        }

        /// <summary>
        /// Build a protobuf message and send it
        /// </summary>
        /// <typeparam name="T">Type of message to build</typeparam>
        /// <param name="build">Callback for the actual building</param>
        /// <returns>Empty task</returns>
        private async Task SendMessage<T>(Action<T> build) where T : IBuilder, new()
        {
            var builder = new T();
            build(builder);
            await this.connection.SendMessage(builder.WeakBuild());
        }

        /// <summary>
        /// Raises the appropriate event for a given message
        /// </summary>
        /// <param name="message">Message to raise event for</param>
        private void OnMessageReceived(IMessage message)
        {
            this.messageEventHandlers[message.GetType()](this, message);
        }
    }
}
