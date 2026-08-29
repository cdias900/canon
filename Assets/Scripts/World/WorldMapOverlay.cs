using System.Collections;
using System.Collections.Generic;
using SheepGate.Art;
using SheepGate.Core;
using SheepGate.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.World
{
    /// <summary>
    /// The distant cities, placed in the world rather than drawn on a panel.
    ///
    /// The opening pulls the camera far enough back that the player sees their own ruined city as
    /// one point among several, with the others plainly shut. Doing this with world-space markers
    /// instead of a UI overlay matters: the camera then performs the whole move as one continuous
    /// push-in from the region to the city, which is what makes "this is one place among many, and
    /// this one is mine" land without a caption explaining it.
    ///
    /// The other cities stay unnamed. Naming seasons we have not built would be a promise; the only
    /// honest thing to show is that something is out there and it is closed.
    /// </summary>
    public sealed class WorldMapOverlay
    {
        sealed class Place
        {
            public string Caption;
            public Vector2 Offset;      // world units from the centre of the village
            public float Size;
        }

        // Portrait framing gives far more vertical room than horizontal, so the neighbours sit
        // mostly above and below rather than beside.
        static readonly Place[] Places =
        {
            new Place { Caption = "fechada", Offset = new Vector2(-14f,  34f), Size = 3.0f },
            new Place { Caption = "fechada", Offset = new Vector2( 16f,  27f), Size = 2.6f },
            new Place { Caption = "fechada", Offset = new Vector2(-17f, -26f), Size = 2.8f },
            new Place { Caption = "fechada", Offset = new Vector2( 15f, -33f), Size = 2.4f }
        };

        /// <summary>Camera size that frames the village and every neighbour around it.</summary>
        public const float FramingSize = 44f;

        readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
        GameObject _root;
        Canvas _canvas;
        Text _heading;
        Text _note;

        public static WorldMapOverlay Show(TilemapBuilder map, Transform parent)
        {
            var overlay = new WorldMapOverlay();
            overlay.Build(map, parent);
            return overlay;
        }

        void Build(TilemapBuilder map, Transform parent)
        {
            Vector3 centre = map != null ? map.CenterWorld() : Vector3.zero;

            _root = new GameObject("WorldMap");
            if (parent != null)
            {
                _root.transform.SetParent(parent, false);
            }

            for (int i = 0; i < Places.Length; i++)
            {
                BuildPlace(Places[i], centre, i);
            }

            // Two lines of framing, and nothing else. The picture is doing the work.
            _canvas = UIKit.CreateCanvas("WorldMapCanvas", 340);
            var root = (RectTransform)_canvas.transform;

            _heading = UIKit.CreateText(root, "Heading", Loc.T("world.map.heading"),
                UIKit.FontSize.Title, UIKit.Palette.Parchment, TextAnchor.MiddleCenter);
            UIKit.AnchorTop((RectTransform)_heading.transform, 80f, 40f, 40f, 120f);

            _note = UIKit.CreateText(root, "Note", Loc.T("world.map.note"),
                UIKit.FontSize.Body, UIKit.Palette.Muted, TextAnchor.MiddleCenter);
            UIKit.AnchorTop((RectTransform)_note.transform, 52f, 40f, 40f, 196f);
        }

        void BuildPlace(Place place, Vector3 centre, int index)
        {
            var host = new GameObject("Place" + index);
            host.transform.SetParent(_root.transform, false);
            host.transform.position = new Vector3(centre.x + place.Offset.x, centre.y + place.Offset.y, 0f);

            // A closed city reads as a shape in the haze, not as a building you could visit.
            SpriteRenderer body = NewRenderer(host.transform, "Body", 60);
            body.sprite = ArtLibrary.Get(ArtKeys.TileHouse);
            body.color = new Color(0.42f, 0.40f, 0.36f, 1f);
            body.transform.localScale = Vector3.one * place.Size;

            SpriteRenderer ring = NewRenderer(host.transform, "Ring", 59);
            ring.sprite = ArtLibrary.Get(ArtKeys.UiPanel);
            ring.color = new Color(0.22f, 0.21f, 0.19f, 0.55f);
            ring.transform.localScale = Vector3.one * (place.Size * 2.1f);
        }

        SpriteRenderer NewRenderer(Transform parent, string name, int order)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            SpriteRenderer renderer = host.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            _renderers.Add(renderer);
            return renderer;
        }

        /// <summary>Dissolves the neighbours as the camera commits to this city.</summary>
        public IEnumerator FadeOut(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float alpha = 1f - Mathf.Clamp01(seconds <= 0f ? 1f : elapsed / seconds);

                for (int i = 0; i < _renderers.Count; i++)
                {
                    SpriteRenderer renderer = _renderers[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    Color colour = renderer.color;
                    colour.a = alpha;
                    renderer.color = colour;
                }

                SetTextAlpha(_heading, alpha);
                SetTextAlpha(_note, alpha);
                yield return null;
            }

            Dispose();
        }

        static void SetTextAlpha(Text text, float alpha)
        {
            if (text == null)
            {
                return;
            }

            Color colour = text.color;
            colour.a = alpha;
            text.color = colour;
        }

        public void Dispose()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            if (_canvas != null)
            {
                Object.Destroy(_canvas.gameObject);
                _canvas = null;
            }

            _renderers.Clear();
        }
    }
}
