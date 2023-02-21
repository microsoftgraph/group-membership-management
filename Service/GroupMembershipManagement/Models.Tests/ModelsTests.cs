using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Models.Tests
{
    [TestClass]
    public class ModelsTests
    {
        [TestMethod]
        public void VerifyModelPackageReferences()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../Models", "Models.csproj");
            var reader = File.ReadAllText(path);
            var doc = XDocument.Parse(reader);
            var packageReferences = doc.XPathSelectElements("//PackageReference");
            Assert.AreEqual(packageReferences.Count(), 0);
        }
    }
}