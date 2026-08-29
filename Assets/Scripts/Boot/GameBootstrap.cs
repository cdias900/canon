using UnityEngine;

namespace SheepGate.Boot
{
    /// <summary>
    /// Scene entry point for Game.unity. The village, the wall, the tilemap and the HUD are
    /// all constructed at runtime by the composer.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            SheepGate.World.GameScene.Compose();
        }
    }
}
