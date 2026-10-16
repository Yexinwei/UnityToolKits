using NUnit.Framework;

namespace Limbo.CurveMotion.Tests
{
    public class EditContextTests
    {
        [TestCase("Coin", 0, "Coin")]
        [TestCase("Coin", 2, "Coin#2")]
        [TestCase("a/b#c%d", 0, "a%2Fb%23c%25d")]
        [TestCase("a/b#c%d", 1, "a%2Fb%23c%25d#1")]
        public void Segment_RoundTrips(string name, int dup, string expected)
        {
            string segment = HierarchyPath.EncodeSegment(name, dup);
            Assert.AreEqual(expected, segment);
            Assert.IsTrue(HierarchyPath.TryDecodeSegment(segment, out var decoded, out int decodedDup));
            Assert.AreEqual(name, decoded);
            Assert.AreEqual(dup, decodedDup);
        }

        [TestCase("Coin#x")]
        [TestCase("Coin%2")]
        [TestCase("Coin%41")]
        public void Segment_RejectsMalformed(string segment)
        {
            Assert.IsFalse(HierarchyPath.TryDecodeSegment(segment, out _, out _));
        }

        [Test]
        public void LastName_DecodesLastSegment()
        {
            Assert.AreEqual("Coin", HierarchyPath.LastName("Canvas/Reward Card/Coin#1"));
            Assert.AreEqual("", HierarchyPath.LastName(""));
            Assert.AreEqual(0, HierarchyPath.Split("").Length);
        }

        [Test]
        public void Context_MainPrefersMover()
        {
            var ctx = new MotionEditContext();
            Assert.IsTrue(ctx.IsEmpty);
            Assert.IsNull(ctx.Main);

            ctx.endTarget.container = "b";
            Assert.AreSame(ctx.endTarget, ctx.Main);
            ctx.mover.container = "a";
            Assert.AreSame(ctx.mover, ctx.Main);
            Assert.IsFalse(ctx.IsEmpty);
        }

        [Test]
        public void Ref_SameAsAndCopy()
        {
            var a = new MotionObjectRef { container = "g", prefab = true, path = "Root/Child", objectId = "" };
            var b = new MotionObjectRef();
            Assert.IsFalse(a.SameAs(b));
            b.CopyFrom(a);
            Assert.IsTrue(a.SameAs(b));
            b.path = "Root/Other";
            Assert.IsFalse(a.SameAs(b));
            b.Clear();
            Assert.IsTrue(b.IsEmpty);
            Assert.IsTrue(b.SameAs(new MotionObjectRef { path = "ignored when empty" }));
            Assert.AreEqual("Child", a.Name);
        }
    }
}
