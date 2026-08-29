using UnityEngine;

namespace SheepGate.Boot
{
    /// <summary>
    /// Scene entry point for Boot.unity. The scene is deliberately near-empty: the whole
    /// boot sequence (data load, scripture load, telemetry, service registration and the
    /// route to CharacterCreation or Game) lives in compiler-checked code.
    /// </summary>
    public class BootLoader : MonoBehaviour
    {
        private void Awake()
        {
            SheepGate.Core.BootSequence.Run();
        }
    }
}
