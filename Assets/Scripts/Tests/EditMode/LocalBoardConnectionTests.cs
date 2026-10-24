using System.Reflection;
using NUnit.Framework;

namespace CalHacks.Tests
{
    public class LocalBoardConnectionTests
    {
        [TestCase("http://127.0.0.1:3000", true)]
        [TestCase("http://localhost:3000", true)]
        [TestCase("http://[::1]:3000", true)]
        [TestCase("https://maincloud.spacetimedb.com", false)]
        [TestCase("http://localhost.example.com:3000", false)]
        [TestCase("file:///tmp/database", false)]
        public void PasswordFreeModeIsForLocalHttpServers(string endpoint, bool local)
        {
            var method = typeof(ThinkInkSession).GetMethod("IsLocalEndpoint", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "same-PC mode must not require a cloud password");
            Assert.AreEqual(local, method.Invoke(null, new object[] { endpoint }));
        }
    }
}
