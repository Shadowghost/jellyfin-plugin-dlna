using System;
using System.Net;
using System.Net.Http;

namespace Rssdp.Infrastructure
{
    /// <summary>
    /// Provides arguments for the <see cref="ISsdpCommunicationsServer.ResponseReceived"/> event.
    /// </summary>
    public sealed class ResponseReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the local IP address.
        /// </summary>
        public IPAddress? LocalIPAddress { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResponseReceivedEventArgs"/> class.
        /// </summary>
        /// <param name="message">The <see cref="HttpResponseMessage"/>.</param>
        /// <param name="receivedFrom">The <see cref="IPEndPoint"/> the message was received from.</param>
        public ResponseReceivedEventArgs(HttpResponseMessage message, IPEndPoint receivedFrom)
        {
            Message = message;
            ReceivedFrom = receivedFrom;
        }

        /// <summary>
        /// Gets the <see cref="HttpResponseMessage"/> that was received.
        /// </summary>
        public HttpResponseMessage Message { get; }

        /// <summary>
        /// Gets the <see cref="IPEndPoint"/> the response came from.
        /// </summary>
        public IPEndPoint ReceivedFrom { get; }
    }
}
