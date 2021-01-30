using System.Collections.Generic;

namespace LotrConquestServerListService {
    public static class Config {
        public static readonly int Port = 2360;
        public static readonly string RequestString = "0A00000A23A4C14D8B21450013769A0EA1B3980EA1B398";
        public static readonly int MaxReceivingTimeMs = 2000;
        public static readonly int UdpReceiveTimeoutMs = 250;
        public static readonly string ServerListResponseCacheKey = "serverListResponse";
        public static readonly string ServerListIsLoadingCacheKey = "serverListIsLoading";
        public static readonly int MemoryCacheExpirationMs = 30000;
        public static readonly int ServerNameIndex = 27; // // server name always begins at index 27 in UDP response
        public static readonly Dictionary<string, string> levelMapping = new Dictionary<string, string> {
            { "black_gates", "???P??R\u0015" },
            { "helms_deep", "?U\\CWUL?" },
            { "isengard", "?????c;?" },
            { "minas_morgul", "???MRe>?" },
            { "minas_tirith", "?s?!???\u007f" },
            { "minas_tirith_top", "??^\u001f????" },
            { "moria", "?\u00176?\u0014???" },
            { "mount_doom", "?]?{?YI?" },
            { "osgiliath", "???????T" },
            { "pelennor_fields", "??;?W\u0006D?" },
            { "rivendell", "?.G?????" },
            { "shire", "\u000f?%?X?c?" },
            { "weathertop", "d#???X&>" },
            // { "...", "Canimrits" } // No idea what "Canimrits" means but sometimes it occures
        };

        public enum ModeIdentifier {
            Tdm = 'x',
            Htdm = '\u0002',
            Cnq = 'v',
            Ctr = 'Y',
            Aslt = '\u0003',
            GoodCampaign = '\u0010',
            EvilCampaign = '\u0017'
        }
    }
}