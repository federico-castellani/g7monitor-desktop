using System;

namespace G7MonitorDesktop.Models
{
    /// <summary>One sensor reading as served by G7 Monitor Server: [unix seconds, mg/dL, trend x10 | null, source].</summary>
    public class GlucoseEntry
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public long UnixTime { get; private set; }
        public int Sgv { get; private set; }
        /// <summary>Rate of change in mg/dL per minute, or null when the sensor gave none.</summary>
        public double? TrendRate { get; private set; }

        public GlucoseEntry(long unixTime, int sgv, double? trendRate)
        {
            UnixTime = unixTime;
            Sgv = sgv;
            TrendRate = trendRate;
        }

        /// <summary>Reading time in local time.</summary>
        public DateTime Timestamp
        {
            get { return Epoch.AddSeconds(UnixTime).ToLocalTime(); }
        }

        public TimeSpan Age
        {
            get { return DateTime.UtcNow - Epoch.AddSeconds(UnixTime); }
        }

        /// <summary>Older than this, the value is no longer shown as current.</summary>
        public bool IsStale
        {
            get { return Age.TotalMinutes > 15; }
        }

        public static long NowUnix()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalSeconds;
        }
    }
}
