using System;

namespace Ameow.Utils
{
    public class TimeUtils
    {
        private static readonly DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long MsSinceEpochToUtcNow()
        {
            var now = DateTime.UtcNow;
            return Convert.ToInt64((now - unixEpoch).TotalMilliseconds);
        }

        public static long MsSinceEpochTo(DateTime timestamp)
        {
            return Convert.ToInt64((timestamp - unixEpoch).TotalMilliseconds);
        }

        public static string TimeStringFromSeconds(double seconds)
        {
            var timeSpan = TimeSpan.FromSeconds(seconds);
            if (timeSpan.Days > 0)
                return string.Format("{0} days {1} hours", timeSpan.Days, timeSpan.Hours);
            else if (timeSpan.Hours > 0)
                return string.Format("{0} hours {1} minutes", timeSpan.Hours, timeSpan.Minutes);
            else if (timeSpan.Minutes > 0)
                return string.Format("{0} minutes {1} seconds", timeSpan.Minutes, timeSpan.Seconds);
            else if (timeSpan.Seconds > 0)
                return string.Format("{0} seconds", timeSpan.Seconds);
            else if (timeSpan.Ticks > 0)
                return "1 second";
            else
                return "0 second";
        }
    }
}