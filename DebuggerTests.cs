using System.IO;
using NUnit.Framework;

namespace KizhiPart3
{
    [TestFixture]
    public class DebuggerTests
    {
        private TextWriter _writer;
        private Debugger _debugger;

        [SetUp]
        public void SetUp()
        {
            _writer = new StringWriter();
            _debugger = new Debugger(_writer);
        }
        
        [Test]
        public void ShouldPrintMem2()
        {
            _debugger.ExecuteLine("def test\r\n    set a 5\r\n    sub a 3\r\n    print b\r\ncall test");
            _debugger.ExecuteLine("add break 2");
            _debugger.ExecuteLine("run");
            _debugger.ExecuteLine("print mem");
            Assert.AreEqual(_writer.ToString(), "a 5 1\r\n");
        }

        [Test]
        public void ShouldDifficultPrintTraceWark()
        {
            _debugger.ExecuteLine(
                "def test\n    set a 5\n    call mocha\ndef govno\n    set b 7\ndef mocha\n    set c 8\n    call govno\ncall test\nprint a");
            _debugger.ExecuteLine("add break 4");
            _debugger.ExecuteLine("run");
            _debugger.ExecuteLine("print mem");
            _debugger.ExecuteLine("print trace");
            _debugger.ExecuteLine("step over");
            _debugger.ExecuteLine("print trace");
            _debugger.ExecuteLine("print mem");
            Assert.AreEqual(_writer.ToString(), "a 5 1\r\nc 8 6\r\n7 govno\r\n2 mocha\r\n8 test\r\na 5 1\r\nc 8 6\r\nb 7 4\r\n");
        }

    }
}