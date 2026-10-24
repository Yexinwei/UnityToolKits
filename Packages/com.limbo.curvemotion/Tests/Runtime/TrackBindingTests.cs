using NUnit.Framework;
using UnityEngine;

namespace Limbo.CurveMotion.Tests
{
    public class TrackBindingTests
    {
        [Test]
        public void ScaleAndRotation_ReadAndWriteTransform()
        {
            var go = new GameObject();
            go.transform.localScale = new Vector3(2f, 2f, 2f);
            var scale = new ScaleTrack();
            Assert.IsTrue(scale.TryReadValue(go, out var v));
            Assert.AreEqual(2f, v.x, 1e-6f);
            scale.WriteValue(go, new Vector4(3f, 3f, 3f, 0f));
            Assert.AreEqual(3f, go.transform.localScale.y, 1e-6f);

            var rotation = new RotationTrack();
            rotation.WriteValue(go, new Vector4(0f, 0f, 45f, 0f));
            Assert.AreEqual(45f, go.transform.localEulerAngles.z, 1e-6f);
        }

        [Test]
        public void Alpha_PrefersCanvasGroup()
        {
            var go = new GameObject();
            var group = go.AddComponent<CanvasGroup>();
            var sprite = go.AddComponent<SpriteRenderer>();
            new AlphaTrack().WriteValue(go, new Vector4(0.25f, 0f, 0f, 0f));
            Assert.AreEqual(0.25f, group.alpha, 1e-6f);
            Assert.AreEqual(1f, sprite.color.a, 1e-6f);
        }

        [Test]
        public void ColorAndAlpha_DoNotOverwriteEachOther()
        {
            var go = new GameObject();
            var sprite = go.AddComponent<SpriteRenderer>();
            new AlphaTrack().WriteValue(go, new Vector4(0.5f, 0f, 0f, 0f));
            new ColorTrack().WriteValue(go, new Vector4(1f, 0f, 0f, 0f));
            Assert.AreEqual(0f, sprite.color.g, 1e-6f);
            Assert.AreEqual(0.5f, sprite.color.a, 1e-6f);
        }

        [Test]
        public void Alpha_SpriteSource_IgnoresCanvasGroup()
        {
            var go = new GameObject();
            var group = go.AddComponent<CanvasGroup>();
            var sprite = go.AddComponent<SpriteRenderer>();
            var track = new AlphaTrack { source = AlphaSource.SpriteRenderer };
            Assert.IsTrue(track.TryReadValue(go, out _));
            track.WriteValue(go, new Vector4(0.25f, 0f, 0f, 0f));
            Assert.AreEqual(1f, group.alpha, 1e-6f, "指定了 SpriteRenderer 就不动 CanvasGroup");
            Assert.AreEqual(0.25f, sprite.color.a, 1e-6f);
        }

        [Test]
        public void Alpha_CanvasGroupSource_WithoutGroup_IsUnsupported()
        {
            var go = new GameObject();
            var sprite = go.AddComponent<SpriteRenderer>();
            var track = new AlphaTrack { source = AlphaSource.CanvasGroup };
            Assert.IsFalse(track.TryReadValue(go, out _), "指定了 CanvasGroup 就不退回到别的组件");
            track.WriteValue(go, new Vector4(0.25f, 0f, 0f, 0f));
            Assert.AreEqual(1f, sprite.color.a, 1e-6f);
        }

        [Test]
        public void Alpha_WithoutComponent_IsUnsupported()
        {
            Assert.IsFalse(new AlphaTrack().TryReadValue(new GameObject(), out _));
        }
    }
}
