using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rssdp.Infrastructure
{
    /// <summary>
    /// Correctly implements the <see cref="IDisposable"/> interface and pattern for an object containing only managed resources, and adds a few common niceties not on the interface such as an <see cref="IsDisposed"/> property.
    /// </summary>
    public abstract class DisposableManagedObjectBase : IDisposable
    {
        /// <summary>
        /// Override this method and dispose any objects you own the lifetime of if disposing is true;
        /// </summary>
        /// <param name="disposing">True if managed objects should be disposed, if false, only unmanaged resources should be released.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
        }

        /// <summary>
        /// Throws and <see cref="ObjectDisposedException"/> if the <see cref="IsDisposed"/> property is true.
        /// </summary>
        /// <seealso cref="IsDisposed"/>
        /// <exception cref="ObjectDisposedException">Thrown if the <see cref="IsDisposed"/> property is true.</exception>
        /// <seealso cref="Dispose()"/>
        protected virtual void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
        }

        /// <summary>
        /// Sets or returns a boolean indicating whether or not this instance has been disposed.
        /// </summary>
        /// <seealso cref="Dispose()"/>
        public bool IsDisposed
        {
            get;
            private set;
        }

        /// <summary>
        /// Builds a message.
        /// </summary>
        /// <param name="header">The header.</param>
        /// <param name="values">The values.</param>
        public static string BuildMessage(string header, Dictionary<string, string> values)
        {
            var builder = new StringBuilder();

            const string ArgFormat = "{0}: {1}\r\n";

            builder.AppendFormat(CultureInfo.InvariantCulture, "{0}\r\n", header);

            foreach (var pair in values)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, ArgFormat, pair.Key, pair.Value);
            }

            builder.Append("\r\n");

            return builder.ToString();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
