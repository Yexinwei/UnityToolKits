using NUnit.Framework;

namespace Limbo.CurveMotion.Tests
{
    public class PackageSmokeTests
    {
        [Test]
        public void SchemaVersion_IsPositive()
        {
            Assert.Greater(CurveMotionInfo.SchemaVersion, 0);
        }
    }
}
