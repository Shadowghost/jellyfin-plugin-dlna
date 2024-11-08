#pragma warning disable CA5394 // Do not use insecure randomness

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rssdp.Infrastructure
{
    /// <summary>
    /// Provides the platform independent logic for publishing SSDP devices (notifications and search responses).
    /// </summary>
    public class SsdpDevicePublisher : DisposableManagedObjectBase, ISsdpDevicePublisher
    {
        private ISsdpCommunicationsServer _commsServer;
        private readonly string _oSName;
        private readonly string _oSVersion;
        private readonly bool _sendOnlyMatchedHost;
        private readonly List<SsdpRootDevice> _devices;

        private Timer? _rebroadcastAliveNotificationsTimer;

        private Dictionary<string, SearchRequest> _recentSearchRequests;

        private readonly Random _random;

        /// <summary>
        /// The log log function.
        /// </summary>
        public Action<string>? LogFunction { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SsdpDevicePublisher"/> class.
        /// </summary>
        /// <param name="communicationsServer">Instance of the <see cref="ISsdpCommunicationsServer"/> interface.</param>
        /// <param name="osName">The OS name.</param>
        /// <param name="osVersion">The OS version.</param>
        /// <param name="sendOnlyMatchedHost">Value indicating whether only matched host should be sent.</param>
        public SsdpDevicePublisher(
            ISsdpCommunicationsServer communicationsServer,
            string? osName,
            string? osVersion,
            bool sendOnlyMatchedHost)
        {
            ArgumentNullException.ThrowIfNull(communicationsServer);
            ArgumentNullException.ThrowIfNullOrEmpty(osName);
            ArgumentNullException.ThrowIfNullOrEmpty(osVersion);

            SupportPnpRootDevice = true;
            _devices = [];
            _recentSearchRequests = new Dictionary<string, SearchRequest>(StringComparer.OrdinalIgnoreCase);
            _random = new Random();

            _commsServer = communicationsServer;
            _commsServer.RequestReceived += CommsServer_RequestReceived;
            _oSName = osName;
            _oSVersion = osVersion;
            _sendOnlyMatchedHost = sendOnlyMatchedHost;

            _commsServer.BeginListeningForMulticast();

            // Send alive notification once on creation
            SendAllAliveNotifications(null);
        }

        /// <summary>
        /// Starts sending alive notifications.
        /// </summary>
        /// <param name="interval">The interval.</param>
        public void StartSendingAliveNotifications(TimeSpan interval)
        {
            _rebroadcastAliveNotificationsTimer = new Timer(SendAllAliveNotifications, null, TimeSpan.FromSeconds(5), interval);
        }

        /// <summary>
        /// Adds a device (and it's children) to the list of devices being published by this server, making them discoverable to SSDP clients.
        /// </summary>
        /// <remarks>
        /// <para>Adding a device causes "alive" notification messages to be sent immediately, or very soon after. Ensure your device/description service is running before adding the device object here.</para>
        /// <para>Devices added here with a non-zero cache life time will also have notifications broadcast periodically.</para>
        /// <para>This method ignores duplicate device adds (if the same device instance is added multiple times, the second and subsequent add calls do nothing).</para>
        /// </remarks>
        /// <param name="device">The <see cref="SsdpDevice"/> instance to add.</param>
        /// <exception cref="ArgumentNullException">Thrown if the <paramref name="device"/> argument is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the <paramref name="device"/> contains property values that are not acceptable to the UPnP 1.0 specification.</exception>
        public void AddDevice(SsdpRootDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);

            ThrowIfDisposed();

            bool wasAdded = false;
            lock (_devices)
            {
                if (!_devices.Contains(device))
                {
                    _devices.Add(device);
                    wasAdded = true;
                }
            }

            if (wasAdded)
            {
                WriteTrace("Device Added", device);

                SendAliveNotifications(device, true, CancellationToken.None);
            }
        }

        /// <inheritdoc />
        public async Task RemoveDevice(SsdpRootDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);

            bool wasRemoved = false;
            lock (_devices)
            {
                if (_devices.Remove(device))
                {
                    wasRemoved = true;
                }
            }

            if (wasRemoved)
            {
                WriteTrace("Device Removed", device);

                await SendByeByeNotifications(device, true, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Returns a read only list of devices being published by this instance.
        /// </summary>
        public IReadOnlyList<SsdpRootDevice> Devices
        {
            get
            {
                return _devices;
            }
        }

        /// <summary>
        /// If true (default) treats root devices as both upnp:rootdevice and pnp:rootdevice types.
        /// </summary>
        /// <remarks>
        /// <para>Enabling this option will cause devices to show up in Microsoft Windows Explorer's network screens (if discovery is enabled etc.). Windows Explorer appears to search only for pnp:rootdeivce and not upnp:rootdevice.</para>
        /// <para>If false, the system will only use upnp:rootdevice for notification broadcasts and and search responses, which is correct according to the UPnP/SSDP spec.</para>
        /// </remarks>
        public bool SupportPnpRootDevice { get; set; }

        /// <summary>
        /// Stops listening for requests, stops sending periodic broadcasts, disposes all internal resources.
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeRebroadcastTimer();

                var commsServer = _commsServer;
                if (commsServer is not null)
                {
                    commsServer.RequestReceived -= CommsServer_RequestReceived;
                }

                var tasks = Devices.ToList().Select(RemoveDevice).ToArray();
                Task.WaitAll(tasks);

                _commsServer = null!;
                if (commsServer is not null)
                {
                    if (!commsServer.IsShared)
                    {
                        commsServer.Dispose();
                    }
                }

                _recentSearchRequests = null!;
            }

            base.Dispose(disposing);
        }

        private async void ProcessSearchRequest(
            string? mx,
            string? searchTarget,
            IPEndPoint remoteEndPoint,
            IPAddress receivedOnlocalIPAddress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(searchTarget))
            {
                WriteTrace(string.Format(CultureInfo.InvariantCulture, "Invalid search request received From {0}, Target is null/empty.", remoteEndPoint?.ToString()));
                return;
            }

            // WriteTrace(String.Format("Search Request Received From {0}, Target = {1}", remoteEndPoint.ToString(), searchTarget));

            if (IsDuplicateSearchRequest(searchTarget, remoteEndPoint))
            {
                // WriteTrace("Search Request is Duplicate, ignoring.");
                return;
            }

            // Wait on random interval up to MX, as per SSDP spec.
            // Also, as per UPnP 1.1/SSDP spec ignore missing/bank MX header. If over 120, assume random value between 0 and 120.
            // Using 16 as minimum as that's often the minimum system clock frequency anyway.
            if (string.IsNullOrEmpty(mx))
            {
                // Windows Explorer is poorly behaved and doesn't supply an MX header value.
                // if (SupportPnpRootDevice)
                mx = "1";
                // else
                // return;
            }

            if (!int.TryParse(mx, out var maxWaitInterval) || maxWaitInterval <= 0)
            {
                return;
            }

            if (maxWaitInterval > 120)
            {
                maxWaitInterval = _random.Next(0, 120);
            }

            // Do not block synchronously as that may tie up a threadpool thread for several seconds.
            var taskScheduler = TaskScheduler.Default;
            await Task.Delay(_random.Next(16, maxWaitInterval * 1000), cancellationToken).ConfigureAwait(false);
            await Task.Run(() =>
            {
                // Copying devices to local array here to avoid threading issues/enumerator exceptions.
                IEnumerable<SsdpDevice>? devices = null;
                lock (_devices)
                {
                    if (string.Equals(SsdpConstants.SsdpDiscoverAllSTHeader, searchTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        devices = GetAllDevicesAsFlatEnumerable().ToArray();
                    }
                    else if (string.Equals(SsdpConstants.UpnpDeviceTypeRootDevice, searchTarget, StringComparison.OrdinalIgnoreCase) || (SupportPnpRootDevice && string.Equals(SsdpConstants.PnpDeviceTypeRootDevice, searchTarget, StringComparison.OrdinalIgnoreCase)))
                    {
                        devices = [.. _devices];
                    }
                    else if (searchTarget.Trim().StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
                    {
                        devices = GetAllDevicesAsFlatEnumerable().Where(d => string.Equals(d.Uuid, searchTarget[5..], StringComparison.OrdinalIgnoreCase)).ToArray();
                    }
                    else if (searchTarget.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
                    {
                        devices = GetAllDevicesAsFlatEnumerable().Where(d => string.Equals(d.FullDeviceType, searchTarget, StringComparison.OrdinalIgnoreCase)).ToArray();
                    }
                }

                if (devices is not null)
                {
                    // WriteTrace(String.Format("Sending {0} search responses", deviceList.Count));

                    foreach (var device in devices)
                    {
                        var root = device.ToRootDevice();

                        if (receivedOnlocalIPAddress is not null &&
                            (!_sendOnlyMatchedHost || (root?.Address is not null && root.Address.Equals(receivedOnlocalIPAddress))))
                        {
                            SendDeviceSearchResponses(device, remoteEndPoint, receivedOnlocalIPAddress, cancellationToken);
                        }
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private IEnumerable<SsdpDevice> GetAllDevicesAsFlatEnumerable()
        {
            return _devices.Union(_devices.SelectManyRecursive<SsdpDevice>((d) => d.Devices));
        }

        private void SendDeviceSearchResponses(
            SsdpDevice device,
            IPEndPoint endPoint,
            IPAddress receivedOnlocalIPAddress,
            CancellationToken cancellationToken)
        {
            bool isRootDevice = device as SsdpRootDevice is not null;
            if (isRootDevice)
            {
                SendSearchResponse(SsdpConstants.UpnpDeviceTypeRootDevice, device, GetUsn(device.Udn, SsdpConstants.UpnpDeviceTypeRootDevice), endPoint, receivedOnlocalIPAddress, cancellationToken);
                if (SupportPnpRootDevice)
                {
                    SendSearchResponse(SsdpConstants.PnpDeviceTypeRootDevice, device, GetUsn(device.Udn, SsdpConstants.PnpDeviceTypeRootDevice), endPoint, receivedOnlocalIPAddress, cancellationToken);
                }
            }

            SendSearchResponse(device.Udn, device, device.Udn, endPoint, receivedOnlocalIPAddress, cancellationToken);

            SendSearchResponse(device.FullDeviceType, device, GetUsn(device.Udn, device.FullDeviceType), endPoint, receivedOnlocalIPAddress, cancellationToken);
        }

        private static string GetUsn(string udn, string fullDeviceType)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}::{1}", udn, fullDeviceType);
        }

        private async void SendSearchResponse(
            string searchTarget,
            SsdpDevice device,
            string uniqueServiceName,
            IPEndPoint endPoint,
            IPAddress receivedOnlocalIPAddress,
            CancellationToken cancellationToken)
        {
            const string Header = "HTTP/1.1 200 OK";

            var rootDevice = device.ToRootDevice();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EXT"] = "",
                ["DATE"] = DateTime.UtcNow.ToString("r"),
                ["HOST"] = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", SsdpConstants.MulticastLocalAdminAddress, SsdpConstants.MulticastPort),
                ["CACHE-CONTROL"] = "max-age = " + rootDevice.CacheLifetime.TotalSeconds,
                ["ST"] = searchTarget,
                ["SERVER"] = string.Format(CultureInfo.InvariantCulture, "{0}/{1} UPnP/1.0 RSSDP/{2}", _oSName, _oSVersion, SsdpConstants.ServerVersion),
                ["USN"] = uniqueServiceName,
                ["LOCATION"] = rootDevice?.Location?.ToString() ?? string.Empty
            };

            var message = BuildMessage(Header, values);

            try
            {
                await _commsServer.SendMessage(
                        Encoding.UTF8.GetBytes(message),
                        endPoint,
                        receivedOnlocalIPAddress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            // WriteTrace(String.Format("Sent search response to " + endPoint.ToString()), device);
        }

        private bool IsDuplicateSearchRequest(string searchTarget, IPEndPoint? endPoint)
        {
            var isDuplicateRequest = false;

            var newRequest = new SearchRequest() { EndPoint = endPoint, SearchTarget = searchTarget, Received = DateTime.UtcNow };
            lock (_recentSearchRequests)
            {
                if (_recentSearchRequests.TryGetValue(newRequest.Key, out var lastRequest))
                {
                    if (lastRequest.IsOld())
                    {
                        _recentSearchRequests[newRequest.Key] = newRequest;
                    }
                    else
                    {
                        isDuplicateRequest = true;
                    }
                }
                else
                {
                    _recentSearchRequests.Add(newRequest.Key, newRequest);
                    if (_recentSearchRequests.Count > 10)
                    {
                        CleanUpRecentSearchRequestsAsync();
                    }
                }
            }

            return isDuplicateRequest;
        }

        private void CleanUpRecentSearchRequestsAsync()
        {
            lock (_recentSearchRequests)
            {
                foreach (var requestKey in (from r in _recentSearchRequests where r.Value.IsOld() select r.Key).ToArray())
                {
                    _recentSearchRequests.Remove(requestKey);
                }
            }
        }

        private void SendAllAliveNotifications(object? state)
        {
            try
            {
                if (IsDisposed)
                {
                    return;
                }

                // WriteTrace("Begin Sending Alive Notifications For All Devices");

                SsdpRootDevice[] devices;
                lock (_devices)
                {
                    devices = _devices.ToArray();
                }

                foreach (var device in devices)
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    SendAliveNotifications(device, true, CancellationToken.None);
                }

                // WriteTrace("Completed Sending Alive Notifications For All Devices");
            }
            catch (ObjectDisposedException ex)
            {
                WriteTrace("Publisher stopped, exception " + ex.Message);
                Dispose();
            }
        }

        private void SendAliveNotifications(SsdpDevice device, bool isRoot, CancellationToken cancellationToken)
        {
            if (isRoot)
            {
                SendAliveNotification(device, SsdpConstants.UpnpDeviceTypeRootDevice, GetUsn(device.Udn, SsdpConstants.UpnpDeviceTypeRootDevice), cancellationToken);
                if (SupportPnpRootDevice)
                {
                    SendAliveNotification(device, SsdpConstants.PnpDeviceTypeRootDevice, GetUsn(device.Udn, SsdpConstants.PnpDeviceTypeRootDevice), cancellationToken);
                }
            }

            SendAliveNotification(device, device.Udn, device.Udn, cancellationToken);
            SendAliveNotification(device, device.FullDeviceType, GetUsn(device.Udn, device.FullDeviceType), cancellationToken);

            foreach (var childDevice in device.Devices)
            {
                SendAliveNotifications(childDevice, false, cancellationToken);
            }
        }

        private void SendAliveNotification(SsdpDevice device, string notificationType, string uniqueServiceName, CancellationToken cancellationToken)
        {
            var rootDevice = device.ToRootDevice();

            const string Header = "NOTIFY * HTTP/1.1";

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // If needed later for non-server devices, these headers will need to be dynamic
                ["HOST"] = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", SsdpConstants.MulticastLocalAdminAddress, SsdpConstants.MulticastPort),
                ["DATE"] = DateTime.UtcNow.ToString("r"),
                ["CACHE-CONTROL"] = "max-age = " + rootDevice.CacheLifetime.TotalSeconds,
                ["LOCATION"] = rootDevice?.Location?.ToString() ?? string.Empty,
                ["SERVER"] = string.Format(CultureInfo.InvariantCulture, "{0}/{1} UPnP/1.0 RSSDP/{2}", _oSName, _oSVersion, SsdpConstants.ServerVersion),
                ["NTS"] = "ssdp:alive",
                ["NT"] = notificationType,
                ["USN"] = uniqueServiceName
            };

            var message = BuildMessage(Header, values);

            _commsServer.SendMulticastMessage(message, _sendOnlyMatchedHost ? rootDevice?.Address : null, cancellationToken);

            // WriteTrace(String.Format("Sent alive notification"), device);
        }

        private Task SendByeByeNotifications(SsdpDevice device, bool isRoot, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            if (isRoot)
            {
                tasks.Add(SendByeByeNotification(device, SsdpConstants.UpnpDeviceTypeRootDevice, GetUsn(device.Udn, SsdpConstants.UpnpDeviceTypeRootDevice), cancellationToken));
                if (SupportPnpRootDevice)
                {
                    tasks.Add(SendByeByeNotification(device, "pnp:rootdevice", GetUsn(device.Udn, "pnp:rootdevice"), cancellationToken));
                }
            }

            tasks.Add(SendByeByeNotification(device, device.Udn, device.Udn, cancellationToken));
            tasks.Add(SendByeByeNotification(device, string.Format(CultureInfo.InvariantCulture, "urn:{0}", device.FullDeviceType), GetUsn(device.Udn, device.FullDeviceType), cancellationToken));

            foreach (var childDevice in device.Devices)
            {
                tasks.Add(SendByeByeNotifications(childDevice, false, cancellationToken));
            }

            return Task.WhenAll(tasks);
        }

        private Task SendByeByeNotification(SsdpDevice device, string notificationType, string uniqueServiceName, CancellationToken cancellationToken)
        {
            const string Header = "NOTIFY * HTTP/1.1";

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // If needed later for non-server devices, these headers will need to be dynamic
                ["HOST"] = string.Format(CultureInfo.InvariantCulture, "{0}:{1}", SsdpConstants.MulticastLocalAdminAddress, SsdpConstants.MulticastPort),
                ["DATE"] = DateTime.UtcNow.ToString("r"),
                ["SERVER"] = string.Format(CultureInfo.InvariantCulture, "{0}/{1} UPnP/1.0 RSSDP/{2}", _oSName, _oSVersion, SsdpConstants.ServerVersion),
                ["NTS"] = "ssdp:byebye",
                ["NT"] = notificationType,
                ["USN"] = uniqueServiceName
            };

            var message = BuildMessage(Header, values);

            var sendCount = IsDisposed ? 1 : 3;
            WriteTrace(string.Format(CultureInfo.InvariantCulture, "Sent byebye notification"), device);
            return _commsServer.SendMulticastMessage(message, sendCount, _sendOnlyMatchedHost ? device.ToRootDevice().Address : null, cancellationToken);
        }

        private void DisposeRebroadcastTimer()
        {
            var timer = _rebroadcastAliveNotificationsTimer;
            _rebroadcastAliveNotificationsTimer = null;
            timer?.Dispose();
        }

        private static string? GetFirstHeaderValue(HttpRequestHeaders? httpRequestHeaders, string headerName)
        {
            string? retVal = null;
            if (httpRequestHeaders is not null && httpRequestHeaders.TryGetValues(headerName, out var values))
            {
                retVal = values?.FirstOrDefault();
            }

            return retVal;
        }

        private void WriteTrace(string text)
        {
            LogFunction?.Invoke(text);
            // System.Diagnostics.Debug.WriteLine(text, "SSDP Publisher");
        }

        private void WriteTrace(string text, SsdpDevice device)
        {
            var rootDevice = device as SsdpRootDevice;
            if (rootDevice is not null)
            {
                WriteTrace(text + " " + device.DeviceType + " - " + device.Uuid + " - " + rootDevice.Location);
            }
            else
            {
                WriteTrace(text + " " + device.DeviceType + " - " + device.Uuid);
            }
        }

        private void CommsServer_RequestReceived(object? sender, RequestReceivedEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (string.Equals(e?.Message?.Method.Method, SsdpConstants.MSearchMethod, StringComparison.OrdinalIgnoreCase))
            {
                // According to SSDP/UPnP spec, ignore message if missing these headers.
                // Edit: But some devices do it anyway
                // if (!e.Message.Headers.Contains("MX"))
                //    WriteTrace("Ignoring search request - missing MX header.");
                // else if (!e.Message.Headers.Contains("MAN"))
                //    WriteTrace("Ignoring search request - missing MAN header.");
                // else
                var endpoint = e?.ReceivedFrom;
                var localIp = e?.LocalIPAddress;
                if (endpoint is not null && localIp is not null)
                {
                    ProcessSearchRequest(GetFirstHeaderValue(e?.Message?.Headers, "MX"), GetFirstHeaderValue(e?.Message?.Headers, "ST"), endpoint, localIp, CancellationToken.None);
                }
            }
        }

        private sealed class SearchRequest
        {
            public IPEndPoint? EndPoint { get; set; }

            public DateTime Received { get; set; }

            public string? SearchTarget { get; set; }

            public string Key
            {
                get { return SearchTarget + ":" + EndPoint; }
            }

            public bool IsOld()
            {
                return DateTime.UtcNow.Subtract(Received).TotalMilliseconds > 500;
            }
        }
    }
}
