namespace SheepGate.Player
{
    /// <summary>
    /// Counting gate for world input. Any module opening a modal panel calls <see cref="Push"/>
    /// and calls <see cref="Pop"/> when it closes; while the count is above zero the player
    /// ignores taps on the world. Nesting is supported because the count, not a boolean, is the
    /// state.
    ///
    /// This is a belt to the braces of <c>EventSystem.IsPointerOverGameObject</c>, which already
    /// swallows taps that land on any raycastable UI.
    /// </summary>
    public static class InputLock
    {
        private static int _count;

        /// <summary>True while at least one modal owner holds the lock.</summary>
        public static bool IsLocked { get { return _count > 0; } }

        public static void Push()
        {
            _count++;
        }

        public static void Pop()
        {
            _count--;
            if (_count < 0) _count = 0;
        }

        /// <summary>Drops every outstanding lock. Call on scene teardown.</summary>
        public static void Clear()
        {
            _count = 0;
        }
    }
}
