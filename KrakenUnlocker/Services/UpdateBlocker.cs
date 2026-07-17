namespace KrakenUnlocker.Services
{
    /// <summary>
    /// Controls update enforcement gates.
    ///
    /// IsUpdatePending = true  → 1 version behind: premium locked, free features work
    /// IsHardBlocked   = true  → 2+ versions behind: ALL features locked, app unusable
    /// </summary>
    public static class UpdateBlocker
    {
        public static bool IsUpdatePending { get; set; } = false;
        public static bool IsHardBlocked   { get; set; } = false;

        /// <summary>
        /// Returns true if the action should be blocked.
        /// premiumOnly = true  → only block if premium required (soft OR hard)
        /// premiumOnly = false → block if hard blocked (free feature gate)
        /// </summary>
        public static bool IsBlocked(bool premiumRequired)
        {
            if (IsHardBlocked) return true;              // always block when hard blocked
            if (premiumRequired && IsUpdatePending) return true; // block premium on soft
            return false;
        }

        public static string GetBlockMessage(bool premiumRequired)
        {
            if (IsHardBlocked)
                return "KrakenXboxUnlocker is out of date. You must update to use this feature.";
            if (premiumRequired && IsUpdatePending)
                return "Please update Kraken to use premium features.";
            return "";
        }
    }
}
