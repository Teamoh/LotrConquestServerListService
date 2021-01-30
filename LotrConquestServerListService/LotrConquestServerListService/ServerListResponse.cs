using System;
using System.Collections.Generic;

namespace LotrConquestServerListService {
    public class ServerListResponse {
        public long Timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        public List<List<Server>> Servers { get; set; }
        public bool IsLoading { get; set; }
    }
}