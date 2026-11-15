using System;

namespace RatAssist
{
    public enum Side { None, Rats, Chef }
    public static class TeamPolicy
    {
        public static bool CanSee(Side viewer, ulong viewerId, Side subject, ulong subjectId)
        {
            if (viewer == Side.Rats) return subject == Side.Rats;
            return viewer == Side.Chef && subject == Side.Chef && viewerId == subjectId;
        }
        public static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value) && Math.Abs(value) < 100000f; }
        public static bool Fresh(long ticks, long now, float lifetime)
        { double seconds = (now - (double)ticks) / TimeSpan.TicksPerSecond; return seconds >= -5 && seconds <= lifetime; }
        // All map projection callers use this plane, never a player's height.
        public const float MapPlaneY = 0f;
    }
}
