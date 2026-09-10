using System;
using System.IO;
using NowUI.Hosting;

namespace NowUI.Standalone.Tests
{
    /// <summary>Preserves the test fixture API over the shared disk resource provider.</summary>
    public sealed class NowStandaloneTestResources : NowFileResources
    {
        public NowStandaloneTestResources() : this(ResolveFixtureRoot())
        {
        }

        public NowStandaloneTestResources(string fixtureRoot) : base(fixtureRoot)
        {
        }

        public string fixtureRoot => resourceRoot;

        private static string ResolveFixtureRoot()
        {
            string beside = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            if (Directory.Exists(beside))
                return beside;

            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Standalone", "Tests", "Fixtures");
                if (Directory.Exists(candidate))
                    return candidate;
            }

            throw new DirectoryNotFoundException("The standalone test fixtures are missing at '" + beside + "'.");
        }
    }
}
