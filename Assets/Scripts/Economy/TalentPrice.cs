namespace SheepGate.Economy
{
    /// <summary>
    /// What a locked wardrobe item costs in talents.
    ///
    /// <b>Derived from the item id, never rolled.</b> The brief asked for a random value between 5
    /// and 15, and a literal <c>Random.Range</c> would have re-rolled on every rebuild of the sheet
    /// — a price that changes while the player is looking at it, and changes again tomorrow. The
    /// number here is a pure function of the id instead: arbitrary-looking across the catalogue,
    /// identical on every open, every device and every save.
    ///
    /// FNV-1a rather than <see cref="string.GetHashCode"/>, which is explicitly not guaranteed
    /// stable between runtimes or runs — on a randomised implementation every player would see
    /// different prices and a player's own prices would move between sessions. This is a price, so
    /// it has to be the same number twice.
    ///
    /// <b>This is a display value, not yet a transaction.</b> Nothing spends talents yet; the
    /// purchase path is deliberately the next change rather than this one. Until it lands, the
    /// sheet shows a price nothing can pay — see the note on rule 7 in
    /// <see cref="SheepGate.UI.BackpackPanel"/>. When purchasing arrives, an authored
    /// <c>price</c> field on the catalogue entry should take over from this function; a hash is
    /// how you get plausible numbers before anyone has balanced them, not how you keep them.
    /// </summary>
    public static class TalentPrice
    {
        /// <summary>Cheapest an item can be, inclusive.</summary>
        public const int Min = 5;

        /// <summary>Dearest an item can be, inclusive.</summary>
        public const int Max = 15;

        const uint FnvOffsetBasis = 2166136261u;
        const uint FnvPrime = 16777619u;

        /// <summary>
        /// The price for one catalogue id, always within [<see cref="Min"/>, <see cref="Max"/>].
        /// An empty or missing id answers <see cref="Min"/> rather than throwing: a row with a
        /// broken id is already logged where it is built, and a second failure here would replace
        /// a cheap wrong number with a blank sheet.
        /// </summary>
        public static int For(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                return Min;
            }

            uint hash = FnvOffsetBasis;
            for (int i = 0; i < itemId.Length; i++)
            {
                hash ^= itemId[i];
                hash *= FnvPrime;
            }

            return Min + (int)(hash % (uint)(Max - Min + 1));
        }
    }
}
