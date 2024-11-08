using System;

namespace Rssdp
{
    /// <summary>
    /// Event arguments for the <see cref="SsdpDevice.DeviceAdded"/> and <see cref="SsdpDevice.DeviceRemoved"/> events.
    /// </summary>
    public sealed class DeviceEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceEventArgs"/> class.
        /// </summary>
        /// <param name="device">The <see cref="SsdpDevice"/> associated with the event this argument class is being used for.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="device"/> argument is null.</exception>
        public DeviceEventArgs(SsdpDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);

            Device = device;
        }

        /// <summary>
        /// Returns the <see cref="SsdpDevice"/> instance the event being raised for.
        /// </summary>
        public SsdpDevice Device { get; }
    }
}
