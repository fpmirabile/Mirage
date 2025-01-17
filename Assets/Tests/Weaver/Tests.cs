using System;
using System.IO;
using System.Linq;
using Mirage.Logging;
using Mono.Cecil;
using NUnit.Framework;
using Unity.CompilationPipeline.Common.Diagnostics;
using UnityEngine;

namespace Mirage.Weaver
{
    public class AssertionMethodAttribute : Attribute { }

    public abstract class TestsBuildFromTestName : Tests
    {
        [SetUp]
        public virtual void TestSetup()
        {
            string className = TestContext.CurrentContext.Test.ClassName.Split('.').Last();

            BuildAndWeaveTestAssembly(className, TestContext.CurrentContext.Test.Name);
        }

        [AssertionMethod]
        protected void IsSuccess()
        {
            Assert.That(weaverLog.Diagnostics, Is.Empty, $"Failed because there are Diagnostics message: \n  {string.Join("\n  ", weaverLog.Diagnostics.Select(x => x.MessageData))}\n");
        }

        [AssertionMethod]
        protected void HasError(string messsage, string atType)
        {
            Assert.That(weaverLog.Diagnostics
                .Where(d => d.DiagnosticType == DiagnosticType.Error)
                .Select(d => d.MessageData), Contains.Item($"{messsage} (at {atType})"));
        }

        [AssertionMethod]
        protected void HasWarning(string messsage, string atType)
        {
            Assert.That(weaverLog.Diagnostics
                .Where(d => d.DiagnosticType == DiagnosticType.Warning)
                .Select(d => d.MessageData), Contains.Item($"{messsage} (at {atType})"));
        }
    }

    [TestFixture]
    public abstract class Tests
    {
        public static readonly ILogger logger = LogFactory.GetLogger<Tests>(LogType.Exception);

        protected Logger weaverLog = new Logger();

        protected AssemblyDefinition assembly;

        protected Assembler assembler;

        protected void BuildAndWeaveTestAssembly(string className, string testName)
        {
            weaverLog.Diagnostics.Clear();
            assembler = new Assembler();

            string testSourceDirectory = className + "~";
            assembler.OutputFile = Path.Combine(testSourceDirectory, testName + ".dll");
            assembler.AddSourceFiles(new string[] { Path.Combine(testSourceDirectory, testName + ".cs") });
            assembly = assembler.Build(weaverLog);

            Assert.That(assembler.CompilerErrors, Is.False);
            foreach (DiagnosticMessage error in weaverLog.Diagnostics)
            {
                // ensure all errors have a location
                Assert.That(error.MessageData, Does.Match(@"\(at .*\)$"));
            }
        }

        [TearDown]
        public void TestCleanup()
        {
            assembler.DeleteOutput();
        }
    }
}
