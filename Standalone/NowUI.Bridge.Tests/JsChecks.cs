// The JavaScript half of W2 and W3, run from `dotnet test`.
//
// js/run.mjs holds twenty-three checks over the recorder, the header, the string tables, the growth protocol, the
// path trie and the three checks of section 3.6. They live in JavaScript because the code they exercise is
// JavaScript; they are driven from here because a bridge whose two halves are tested by two commands is a bridge
// whose two halves get tested at two different times.

using NUnit.Framework;

namespace NowUI.Bridge.Tests
{
    [TestFixture]
    public sealed class JsChecks
    {
        [Test]
        public void TheRecorderAndTrieChecksPass()
        {
            string output = Node.Run();
            TestContext.WriteLine(output);

            StringAssert.Contains("0 failed.", output);
            StringAssert.DoesNotContain("  FAIL  ", output);
        }
    }
}
