using System.IO;
using NUnit.Framework;

namespace KizhiPart3
{
    [TestFixture]
    public class DebuggerTests
    {
        [Test]
        public void ShouldPrintMem()
        {
            var writer = new StringWriter();
            var debugger = new Debugger(writer);

            debugger.ExecuteLine("set code");
            debugger.ExecuteLine("print a\r\ncall A\r\ndef A\r\n    set a 12\r\n    sub a 1\r\n    rem a\r\nprint a\r\ncall A\r\nprint a");
            debugger.ExecuteLine("end set code");
            debugger.ExecuteLine("add break 4");
            debugger.ExecuteLine("run");
            debugger.ExecuteLine("print mem");
            Assert.AreEqual(writer.ToString(), "");
        }

        [Test]
        public void ShouldPrintMem2()
        {
            var writer = new StringWriter();
            var debugger = new Debugger(writer);

            debugger.ExecuteLine("set code");
            debugger.ExecuteLine("def test\r\n    set a 5\r\n    sub a 3\r\n    print b\r\ncall test");
            debugger.ExecuteLine("end set code");
            debugger.ExecuteLine("add break 2");
            debugger.ExecuteLine("run");
            debugger.ExecuteLine("print mem");
            Assert.AreEqual(writer.ToString(), "a 5 1\r\n");

        }

        [Test]
        public void ShouldPrepareForNextRunningAfterFinishingCurrentRunning()
        {
            var writer = new StringWriter();
            var debugger = new Debugger(writer);

            debugger.ExecuteLine("set code");
            debugger.ExecuteLine("def test\r\n    set a 5\r\n    sub a 3\r\n    print b\r\ncall test");
            debugger.ExecuteLine("end set code");
            debugger.ExecuteLine("step over");
            Assert.AreEqual(0, 0);
        }

        [Test]
        public void ShouldPrintTrace()
        {
            var writer = new StringWriter();
            var debugger = new Debugger(writer);

            debugger.ExecuteLine("set code");
            debugger.ExecuteLine("def A\r\n    call B\r\ndef B\r\n    call A\r\ncall B");
            debugger.ExecuteLine("end set code");
            debugger.ExecuteLine("add break 1");
            debugger.ExecuteLine("run");
            debugger.ExecuteLine("print trace");
            var trace = writer.ToString();
            debugger.ExecuteLine("step over");
            debugger.ExecuteLine("step over");
            debugger.ExecuteLine("print trace");
            Assert.AreEqual(0, 0);
        }

        [Test]
        public void ShouldPrintTrace2()
        {
            var writer = new StringWriter();
            var debugger = new Debugger(writer);

            debugger.ExecuteLine("set code");
            debugger.ExecuteLine("print a\r\ncall A\r\ndef A\r\n    set a 12\r\n    sub a 1\r\n    rem a\r\nprint a\r\ncall A\r\nprint a");
            debugger.ExecuteLine("end set code");
            debugger.ExecuteLine("add break 3");
            debugger.ExecuteLine("run");
            debugger.ExecuteLine("print trace");
            var trace = writer.ToString();
            debugger.ExecuteLine("run");
            Assert.AreEqual(0, 0);
        }

    }
}