using System;
using System.Net;
using System.Net.Http;

namespace Rssdp.Infrastructure
{
    /// <summary>
    /// Provides arguments for the <see cref="ISsdpCommunicationsServer.RequestReceived"/> event.
    /// </summary>
    public sealed class RequestReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the local IP address.
        /// </summary>
        public IPAddress LocalIPAddress { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceAvailableEventArgs"/> class.
        /// </summary>
        /// <param name="message">The <see cref="HttpRequestMessage"/>. data</param>
        /// <param name="receivedFrom">The <see cref="IPEndPoint"/> the message was received from.</param>
        /// <param name="localIPAddress">The local <see cref="IPAddress"/>.</param>
        public RequestReceivedEventArgs(HttpRequestMessage? message, IPEndPoint receivedFrom, IPAddress localIPAddress)
        {
            Message = message;
            ReceivedFrom = receivedFrom;
            LocalIPAddress = localIPAddress;
        }

        /// <summary>
        /// Gets the <see cref="HttpRequestMessage"/> that was received.
        /// </summary>
        public HttpRequestMessage? Message { get; }

        /// <summary>
        /// Gets the <see cref="IPEndPoint"/> the request came from.
        /// </summary>
        public IPEndPoint ReceivedFrom { get; }
    }
}
