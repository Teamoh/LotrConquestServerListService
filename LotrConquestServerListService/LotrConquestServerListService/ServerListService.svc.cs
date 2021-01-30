using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Caching;
using System.Security.Cryptography;
using static LotrConquestServerListService.Config;

namespace LotrConquestServerListService {
    public class ServerListService : IServerListService {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public Server GetServer() {
            log.Debug("In method GetServer");

            return new Server {
                Name = "Test",
                Slots = 16,
                Players = 10
            };
        }

        public List<string> ResetIsLoading() {
            log.Debug("In method ResetIsLoading");

            var cache = MemoryCache.Default;
            cache.Set(ServerListIsLoadingCacheKey, "false", new CacheItemPolicy());
            return new List<string>() { "ok" };
        }

        public Task<ServerListResponse> GetServerList() {
            log.Debug("In method GetServerList");

            var cache = MemoryCache.Default;
            var serverListIsLoading = (cache[ServerListIsLoadingCacheKey] as string) == "true";
            Task<ServerListResponse> responseTask;

            if (serverListIsLoading) {
                log.Debug("Server list is currently loading");

                responseTask = Task.Run(() => {
                    return new ServerListResponse {
                        IsLoading = true // tells the frontend to try again later
                    };
                });
            } else if (!(cache[ServerListResponseCacheKey] is ServerListResponse cachedResponse)) {
                log.Info("Start loading the server list");

                cache.Set(ServerListIsLoadingCacheKey, "true", new CacheItemPolicy());

                var port = Port;
                var requestData = StringToByteArray(RequestString);
                var udpClient = new UdpClient(port);
                var maxReceivingTimeSpan = TimeSpan.FromMilliseconds(MaxReceivingTimeMs);

                responseTask = Task.Run(() => {
                    var serverDictionary = new Dictionary<string, Dictionary<string, Server>>();
                    var stopwatch = new Stopwatch();

                    udpClient.Client.ReceiveTimeout = UdpReceiveTimeoutMs;
                    stopwatch.Start();

                    // listen to UDP responses for given timespan
                    while (stopwatch.Elapsed < maxReceivingTimeSpan) {
                        try {
                            var anyEndpoint = new IPEndPoint(IPAddress.Any, 0);
                            var responseBytes = udpClient.Receive(ref anyEndpoint);
                            var anyIpAddress = anyEndpoint.Address;
                            var receiveString = Encoding.ASCII.GetString(responseBytes);
                            var server = CreateServer(anyIpAddress, receiveString);

                            if (server == null) {
                                continue;
                            }

                            var anyIpAddressStr = anyIpAddress.ToString();

                            if (!serverDictionary.ContainsKey(anyIpAddressStr)) {
                                serverDictionary[anyIpAddressStr] = new Dictionary<string, Server>();
                            }

                            var serverName = server.Name;

                            if (serverDictionary[anyIpAddressStr].ContainsKey(serverName)) {
                                // dictionary already contains a server with this server id
                                // so discard the current server
                                continue;
                            }

                            // add server to dictionary
                            // Please note:
                            // The mapping must be done with the server name as key
                            // because otherwise servers may occure duplicated
                            // if another player accesses the ingame server list
                            // while this service is collecting the data
                            serverDictionary[anyIpAddressStr].Add(serverName, server);
                        } catch {
                            // probably udp receive timeout
                            // no action required
                        }
                    }

                    stopwatch.Stop();
                    udpClient.Dispose();

                    var serverList = new List<List<Server>>();

                    foreach (var keyValuePair in serverDictionary) {
                        var subServerList = new List<Server>();
                        var subServerDictionary = keyValuePair.Value;

                        foreach (var innerKeyValuePair in subServerDictionary) {
                            subServerList.Add(innerKeyValuePair.Value);
                        }

                        serverList.Add(subServerList);
                    }

                    var serverListResponse = new ServerListResponse {
                        Servers = serverList,
                    };

                    var cachePolicy = new CacheItemPolicy {
                        AbsoluteExpiration = DateTimeOffset.UtcNow.AddMilliseconds(MemoryCacheExpirationMs)
                    };

                    cache.Set(ServerListResponseCacheKey, serverListResponse, cachePolicy);
                    cache.Set(ServerListIsLoadingCacheKey, "false", new CacheItemPolicy());

                    var hostCount = serverList.Count;
                    var serverCount = serverList.Sum(x => x.Count);
                    var playerCount = serverList.Sum(h => h.Sum(s => s.Players));
                    var slotCount = serverList.Sum(h => h.Sum(s => s.Slots));
                    log.Info($"Finished loading the server list ({hostCount} hosts, {serverCount} servers, {playerCount}/{slotCount} players)");

                    return serverListResponse;
                });

                // send broadtcast message
                udpClient.Send(requestData, requestData.Length, new IPEndPoint(IPAddress.Broadcast, port));
            } else {
                log.Debug("Cached response is used");

                responseTask = Task.Run(() => {
                    return cachedResponse;
                });
            }

            return responseTask;
        }

        private byte[] StringToByteArray(string hex) {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        private Server CreateServer(IPAddress ipAddress, string receiveString) {
            // Example receiveStrings:
            //"\n\0\u0001\n#??M?!E\0\u0013v?\0\0\0\0\0\0.|\0\0\0\u001fMiddle-earth Fun | Mixed | 2020\0\0\0\0\0\0\0\u0010v??<???????T\0\0\0\0"
            // "\n\0\u0001\n#??M?!E\0\u0013v?\0\0\0\0\0\0.}\0\0\0\u001eMiddle-earth Fun | Duel | 2020\0\0\0\0\0\0\0\u0006x'?*??^\u001f????\0\0\0\0"
            // "\n\0\u0001\n#??M?!E\0\u0013v?\0\0\0\0\0\0.|\0\0\0\u000fCNQReboot Mixed\0\0\0\0\0\0\0\u0010v??<??;?W\u0006D?\0\0\0\0"
            // "\n\0\u0001\n#??M?!E\0\u0013v?\0\0\0\0\0\0.}\0\0\0\u0012CNQReboot Duelling\0\0\0\0\0\0\0\bx'?*?.G?????\0\0\0\0"

            var receiveStringLength = receiveString.Length;

            if (receiveStringLength < 40) {
                return null;
            }

            try {
                var serverId = HashMD5(ipAddress + "~" + receiveString).ToLower();
                var serverNamePart = receiveString.Substring(ServerNameIndex);
                var serverNameEndIndex = serverNamePart.IndexOf('\0');
                var serverName = serverNamePart.Substring(0, serverNameEndIndex).Trim();
                var serverEndPart = serverNamePart.Substring(serverNameEndIndex);
                var playerCount = (int)serverEndPart[3];
                var slots = (int)serverEndPart[7];
                var mode = ParseMode(serverEndPart[8]);
                var levelPart = serverEndPart.Substring(9);
                var level = ParseLevel(levelPart);

                if (mode == null) {
                    var writeableReceiveString = GetWriteableReceiveString(receiveString);
                    var unicodeReceiveString = GetUnicodeReceiveString(receiveString);
                    log.Warn($"Could not parse mode for receiveString '{writeableReceiveString}', Unicodes: '{unicodeReceiveString}'");
                }

                if (level == null) {
                    var writeableReceiveString = GetWriteableReceiveString(receiveString);
                    var unicodeReceiveString = GetUnicodeReceiveString(receiveString);
                    log.Warn($"Could not parse level for receiveString '{writeableReceiveString}', Unicodes: '{unicodeReceiveString}'");
                }

                var server = new Server {
                    Id = serverId,
                    Name = serverName,
                    Level = level,
                    Mode = mode,
                    Slots = slots,
                    Players = playerCount
                };

                return server;
            } catch (Exception exc) {
                var writeableReceiveString = GetWriteableReceiveString(receiveString);
                var unicodeReceiveString = GetUnicodeReceiveString(receiveString);
                log.Error($"Failed to create server for receiveString '{writeableReceiveString}', Unicodes: '{unicodeReceiveString}', Exception: '{exc.Message}'");
            }

            return null;
        }

        private string HashMD5(string input) {
            using (MD5 md5 = MD5.Create()) {
                var inputBytes = Encoding.ASCII.GetBytes(input);
                var hashBytes = md5.ComputeHash(inputBytes);

                // Convert the byte array to hexadecimal string
                var sb = new StringBuilder();

                for (int i = 0; i < hashBytes.Length; i++) {
                    sb.Append(hashBytes[i].ToString("X2"));
                }

                return sb.ToString();
            }
        }

        private static string ParseMode(char input) {
            switch (input) {
                case (char)ModeIdentifier.Tdm:
                    return "tdm";
                case (char)ModeIdentifier.Htdm:
                    return "htdm";
                case (char)ModeIdentifier.Cnq:
                    return "cnq";
                case (char)ModeIdentifier.Ctr:
                    return "ctr";
                case (char)ModeIdentifier.Aslt:
                    return "aslt";
                case (char)ModeIdentifier.GoodCampaign:
                    return "gcam"; // only works in lobby
                case (char)ModeIdentifier.EvilCampaign:
                    return "ecam"; // only works in lobby
                default:
                    return null;
            }
        }

        private static string ParseLevel(string levelPart) {
            if (string.IsNullOrEmpty(levelPart)) {
                return null;
            }

            foreach (var keyValue in levelMapping) {
                var levelIdentifier = keyValue.Value;

                if (levelPart.Contains(levelIdentifier)) {
                    var levelName = keyValue.Key;
                    return levelName;
                }
            }

            return null;
        }

        private static string GetWriteableReceiveString(string receiveString) {
            return receiveString
                    .Replace("\n", @"\n")
                    .Replace("\0", @"\0")
                    .Replace("\u0001", @"\_u0001")
                    .Replace("\u0002", @"\_u0002")
                    .Replace("\u0003", @"\_u0003")
                    .Replace("\u0004", @"\_u0004")
                    .Replace("\u0005", @"\_u0005")
                    .Replace("\u0006", @"\_u0006")
                    .Replace("\u0007", @"\_u0007")
                    .Replace("\u0008", @"\_u0008")
                    .Replace("\u0009", @"\_u0009")
                    .Replace("\u0013", @"\_u0013")
                    .Replace("\u007f", @"\_u007f")
                    .Replace("\u0002", @"\_u0002")
                    .Replace("\u0014", @"\_u0014")
                    .Replace("\u0017", @"\_u0017")
                    .Replace("\u0006", @"\_u0006")
                    .Replace("\u000f", @"\_u000f")
                    .Replace("\u0015", @"\_u0015")
                    .Replace("\u0010", @"\_u0010")
                    .Replace("\u001f", @"\_u001f");
        }

        private static string GetUnicodeReceiveString(string receiveString) {
            var unicodeReceiveStringBuilder = new StringBuilder();

            foreach (var character in receiveString) {
                unicodeReceiveStringBuilder.Append(((int)character) + " ");
            }

            return unicodeReceiveStringBuilder.ToString().Trim();
        }
    }
}
