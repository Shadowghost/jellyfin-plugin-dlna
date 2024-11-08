using System;
using System.Net;

namespace Rssdp
{
    /// <summary>
    /// Event arguments for the <see cref="Infrastructure.SsdpDeviceLocator.DeviceAvailable"/> event.
    /// </summary>
    public sealed class DeviceAvailableEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the remote IP address.
        /// </summary>
        public IPAddress? RemoteIPAddress { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceAvailableEventArgs"/> class.
        /// </summary>
        /// <param name="discoveredDevice">A <see cref="DiscoveredSsdpDevice"/> instance representing the available device.</param>
        /// <param name="isNewlyDiscovered">A boolean value indicating whether or not this device came from the cache. See <see cref="IsNewlyDiscovered"/> for more detail.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="discoveredDevice"/> parameter is null.</exception>
        public DeviceAvailableEventArgs(DiscoveredSsdpDevice discoveredDevice, bool isNewlyDiscovered)
        {
            ArgumentNullException.ThrowIfNull(discoveredDevice);

            DiscoveredDevice = discoveredDevice;
            IsNewlyDiscovered = isNewlyDiscovered;
        }

        /// <summary>
        /// Gets a value indicating whether the device was discovered due to an alive notification, or a search and was not already in the cache.
        /// If the item comes from the cache it is not considered newly discovered.
        /// </summary>
        public bool IsNewlyDiscovered { get; }

        /// <summary>
        /// A reference to a <see cref="DiscoveredSsdpDevice"/> instance containing the discovered details and allowing access to the full device description.
        /// </summary>
        public DiscoveredSsdpDevice DiscoveredDevice { get; }
    }
}
